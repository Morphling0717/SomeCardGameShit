// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class BattlefieldRaycastInput : Node
{
    public const float DragThresholdPixels = 8.0f;

    private static readonly StringName ColliderKey = new("collider");
    private BattlefieldCameraRig? _camera;
    private IBattlefieldPickTarget? _pressedTarget;
    private BattlefieldSurfaceRef? _pressedSurface;
    private IBattlefieldPickTarget? _hoveredTarget;
    private Vector2 _pressPosition;
    private ulong _pressedRevision;
    private bool _dragging;

    public event Action<ulong, BattlefieldSurfaceRef>? Clicked;

    public event Action<ulong, BattlefieldSurfaceRef, BattlefieldSurfaceRef>? DragCompleted;

    public event Action<ulong, BattlefieldSurfaceRef>? DragStarted;

    public event Action? DragCancelled;

    public event Action<BattlefieldSurfaceHoverEventArgs>? HoverChanged;

    public event Action<BattlefieldSurfaceHoverEventArgs>? SecondaryClicked;

    public event Action<Vector3>? PointerWorldChanged;

    public Func<ulong>? RevisionProvider { get; set; }

    public Func<Vector2, bool>? GuiBlocksPointer { get; set; }

    public bool InputEnabled { get; private set; }

    public bool HasActiveDrag => _pressedTarget is not null;

    public bool IsDragging => _dragging;

    public void Configure(BattlefieldCameraRig camera, Func<ulong> revisionProvider)
    {
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        RevisionProvider = revisionProvider ?? throw new ArgumentNullException(nameof(revisionProvider));
    }

    public void SetInputEnabled(bool enabled)
    {
        InputEnabled = enabled;
        if (!enabled)
        {
            CancelTransient();
        }
    }

    public void CancelTransient()
    {
        bool hadDrag = _pressedTarget is not null;
        _pressedTarget = null;
        _pressedSurface = null;
        _pressedRevision = 0;
        _dragging = false;
        SetHovered(null);
        if (hadDrag)
        {
            DragCancelled?.Invoke();
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (!InputEnabled || _camera is null || RevisionProvider is null)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseMotion motion:
                HandleMotion(motion);
                break;
            case InputEventMouseButton button:
                HandleMouseButton(button);
                break;
        }
    }

    public bool CiTryPick(Vector2 screenPosition, out BattlefieldSurfaceRef surface) =>
        TryPickSurface(screenPosition, out surface);

    public bool TryPickSurface(Vector2 screenPosition, out BattlefieldSurfaceRef surface)
    {
        IBattlefieldPickTarget? target = Pick(screenPosition);
        if (target?.Surface is { } picked)
        {
            surface = picked;
            return true;
        }

        surface = default;
        return false;
    }

    public bool CiClickAt(Vector2 screenPosition)
    {
        IBattlefieldPickTarget? target = Pick(screenPosition);
        if (!InputEnabled || GuiBlocks(screenPosition) ||
            target?.Surface is null || !target.CanActivate)
        {
            return false;
        }

        BeginPress(screenPosition);
        return _pressedTarget is not null && EndPress(screenPosition);
    }

    public bool CiDragAt(Vector2 sourcePosition, Vector2 destinationPosition)
    {
        IBattlefieldPickTarget? source = Pick(sourcePosition);
        IBattlefieldPickTarget? destination = Pick(destinationPosition);
        if (!InputEnabled || GuiBlocks(sourcePosition) || GuiBlocks(destinationPosition) ||
            source?.Surface is null || !source.CanActivate ||
            destination?.Surface is null ||
            sourcePosition.DistanceTo(destinationPosition) < DragThresholdPixels)
        {
            return false;
        }

        BeginPress(sourcePosition);
        BeginDragging();
        if (TryProjectToBoard(destinationPosition, out Vector3 world))
        {
            PointerWorldChanged?.Invoke(world);
        }

        return EndPress(destinationPosition);
    }

    internal void CiArmDragToken(IBattlefieldPickTarget target, ulong revision)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.Surface is null)
        {
            throw new ArgumentException("A synthetic drag target must expose a surface.", nameof(target));
        }

        _pressedTarget = target;
        _pressedSurface = target.Surface;
        _pressedRevision = revision;
        _dragging = true;
    }

    private void HandleMotion(InputEventMouseMotion motion)
    {
        if (GuiBlocks(motion.Position))
        {
            SetHovered(null);
            return;
        }

        IBattlefieldPickTarget? target = Pick(motion.Position);
        SetHovered(target);
        if (_pressedTarget is not null &&
            !_dragging &&
            motion.Position.DistanceTo(_pressPosition) >= DragThresholdPixels)
        {
            BeginDragging();
        }

        if (_dragging && TryProjectToBoard(motion.Position, out Vector3 world))
        {
            PointerWorldChanged?.Invoke(world);
        }
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && button.Pressed)
        {
            if (!GuiBlocks(button.Position) && _camera!.AdjustWheel(button.ButtonIndex))
            {
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (button.ButtonIndex == MouseButton.Right && button.Pressed)
        {
            if (GuiBlocks(button.Position))
            {
                return;
            }

            IBattlefieldPickTarget? target = Pick(button.Position);
            if (target is not null)
            {
                SecondaryClicked?.Invoke(CreateHoverArgs(target));
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        if (button.Pressed)
        {
            BeginPress(button.Position);
        }
        else
        {
            EndPress(button.Position);
        }
    }

    private void BeginPress(Vector2 position)
    {
        CancelPressWithoutNotification();
        if (GuiBlocks(position))
        {
            return;
        }

        IBattlefieldPickTarget? target = Pick(position);
        if (target?.Surface is null || !target.CanActivate)
        {
            return;
        }

        _pressedTarget = target;
        _pressedSurface = target.Surface;
        _pressPosition = position;
        _pressedRevision = RevisionProvider!();
        _dragging = false;
        GetViewport().GuiReleaseFocus();
        GetViewport().SetInputAsHandled();
    }

    private bool EndPress(Vector2 position)
    {
        if (_pressedTarget is null || _pressedSurface is not { } source)
        {
            return false;
        }

        ulong currentRevision = RevisionProvider!();
        bool stale = currentRevision != _pressedRevision;
        bool blocked = GuiBlocks(position);
        IBattlefieldPickTarget? destination = blocked ? null : Pick(position);
        bool wasDragging = _dragging;
        CancelPressWithoutNotification();

        if (stale || blocked)
        {
            DragCancelled?.Invoke();
            return false;
        }

        if (!wasDragging)
        {
            Clicked?.Invoke(currentRevision, source);
            GetViewport().SetInputAsHandled();
            return true;
        }

        if (destination?.Surface is { } destinationSurface)
        {
            DragCompleted?.Invoke(currentRevision, source, destinationSurface);
            GetViewport().SetInputAsHandled();
            return true;
        }

        DragCancelled?.Invoke();
        return false;
    }

    private IBattlefieldPickTarget? Pick(Vector2 screenPosition)
    {
        if (_camera is null || !IsInsideTree())
        {
            return null;
        }

        Vector3 origin = _camera.ProjectRayOrigin(screenPosition);
        Vector3 end = origin + (_camera.ProjectRayNormal(screenPosition) * 100.0f);
        PhysicsRayQueryParameters3D query = PhysicsRayQueryParameters3D.Create(
            origin,
            end,
            CardActor3D.PickCollisionLayer);
        query.CollideWithAreas = true;
        query.CollideWithBodies = false;
        Godot.Collections.Dictionary hit = _camera.GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (!hit.TryGetValue(ColliderKey, out Variant collider))
        {
            return null;
        }

        return collider.AsGodotObject() as IBattlefieldPickTarget;
    }

    private bool TryProjectToBoard(Vector2 screenPosition, out Vector3 position)
    {
        Vector3 origin = _camera!.ProjectRayOrigin(screenPosition);
        Vector3 direction = _camera.ProjectRayNormal(screenPosition);
        if (Mathf.IsZeroApprox(direction.Y))
        {
            position = default;
            return false;
        }

        float distance = -origin.Y / direction.Y;
        if (distance < 0.0f)
        {
            position = default;
            return false;
        }

        position = origin + (direction * distance);
        return true;
    }

    private bool GuiBlocks(Vector2 position)
    {
        if (GuiBlocksPointer is not null)
        {
            return GuiBlocksPointer(position);
        }

        for (Control? control = GetViewport().GuiGetHoveredControl();
             control is not null;
             control = control.GetParentOrNull<Control>())
        {
            if (control.IsInGroup("scgs_battlefield_passthrough"))
            {
                return false;
            }

            if (control.IsInGroup("scgs_blocks_battlefield_input") ||
                control is BaseButton or LineEdit or TextEdit or ItemList or Tree or Godot.Range)
            {
                return true;
            }
        }

        return false;
    }

    private void SetHovered(IBattlefieldPickTarget? target)
    {
        if (ReferenceEquals(_hoveredTarget, target))
        {
            return;
        }

        _hoveredTarget?.SetPointerHovered(false);
        _hoveredTarget = target;
        _hoveredTarget?.SetPointerHovered(true);
        HoverChanged?.Invoke(target is null
            ? new BattlefieldSurfaceHoverEventArgs(null, null, Vector3.Zero)
            : CreateHoverArgs(target));
    }

    private static BattlefieldSurfaceHoverEventArgs CreateHoverArgs(
        IBattlefieldPickTarget target) => new(
            target.Surface,
            target.CardPresentation,
            target.WorldAnchor);

    private void CancelPressWithoutNotification()
    {
        _pressedTarget = null;
        _pressedSurface = null;
        _pressedRevision = 0;
        _dragging = false;
    }

    private void BeginDragging()
    {
        if (_dragging || _pressedTarget is null || _pressedSurface is not { } source)
        {
            return;
        }

        _dragging = true;
        DragStarted?.Invoke(_pressedRevision, source);
    }
}
