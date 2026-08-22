using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;
using Scgs.Hotseat;

namespace Scgs.GodotClient.UI;

public sealed partial class SnapshotSlot : Button
{
    private static readonly StringName DragMarker = new("scgs_direct_drag");

    private Label _label = null!;
    private ulong? _knownInstanceId;
    private bool _hasCard;
    private bool _identityHidden;
    private bool _dragEnabled;
    private bool _dropEnabled;
    private string _displayText = string.Empty;
    private SnapshotAffordance _affordance;
    private bool _smokeDrag;

    public event Action<SnapshotSlot>? Activated;

    public event Action<SnapshotSlot>? Hovered;

    public event Action<SnapshotSlot>? SecondaryActivated;

    public event Action<SnapshotSlot>? DragStarted;

    public event Action<SnapshotSlot>? DropReceived;

    public string ZoneName { get; private set; } = string.Empty;

    public int SlotIndex { get; private set; } = -1;

    public bool HasCard => _hasCard;

    public bool HasKnownIdentity => _hasCard && !_identityHidden && _knownInstanceId.HasValue;

    public ulong? KnownInstanceId => HasKnownIdentity ? _knownInstanceId : null;

    public bool IsSelected => ButtonPressed;

    public SnapshotAffordance Affordance => _affordance;

    internal bool IsInteractionDisabledForSmoke =>
        Disabled && !_dragEnabled && !_dropEnabled && _affordance == SnapshotAffordance.None;

    internal bool HasTooltipForSmoke => !string.IsNullOrEmpty(TooltipText);

    internal bool HasMetadataForSmoke => GetMetaList().Count != 0;

    public override void _Ready()
    {
        _label = GetNode<Label>("%SlotLabel");
        ToggleMode = true;
        Disabled = true;
        FocusMode = FocusModeEnum.None;
        Pressed += OnPressed;
        MouseEntered += () => Hovered?.Invoke(this);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton
            {
                ButtonIndex: MouseButton.Right,
                Pressed: true,
            })
        {
            AcceptEvent();
            SecondaryActivated?.Invoke(this);
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!_dragEnabled || Disabled)
        {
            return default;
        }

        DragStarted?.Invoke(this);

        if (!_smokeDrag)
        {
            var preview = new Label
            {
                Text = _identityHidden ? "牌背" : _displayText,
                CustomMinimumSize = new Vector2(132, 58),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = new Color(1.0f, 1.0f, 1.0f, 0.88f),
            };
            SetDragPreview(preview);
        }

        var payload = new Godot.Collections.Dictionary
        {
            [DragMarker] = true,
        };
        return Variant.From(payload);
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!_dropEnabled || data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        Godot.Collections.Dictionary payload = data.AsGodotDictionary();
        return payload.ContainsKey(DragMarker) && payload[DragMarker].AsBool();
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (_CanDropData(atPosition, data))
        {
            DropReceived?.Invoke(this);
        }
    }

    public void ShowEmpty(string zoneName, int index, bool selectable = false)
    {
        ZoneName = zoneName;
        SlotIndex = index;
        _hasCard = false;
        _identityHidden = false;
        _knownInstanceId = null;
        _displayText = $"{zoneName} {index + 1}\n— 空 —";
        TooltipText = string.Empty;
        SetSelectable(selectable);
        SetSelected(false);
        SetAffordance(SnapshotAffordance.None);
    }

    public void ShowCard(CardView card, string zoneName, int index, bool selectable = false)
    {
        ZoneName = zoneName;
        SlotIndex = index;
        _hasCard = true;
        _identityHidden = CardPresentation.IsIdentityHidden(card);
        _knownInstanceId = _identityHidden ? null : card.InstanceId;

        string title = _identityHidden ? "伏策（背面）" : card.Name;
        string detail = card.Kind switch
        {
            CardKind.Unit => $"{card.CurrentAttack} / {card.CurrentHealth}",
            CardKind.Relic => $"倒计时 {card.Countdown}",
            CardKind.Trap => _identityHidden ? "身份已隐藏" : "伏策",
            CardKind.Spell => $"费用 {card.Cost}",
            null => "身份已隐藏",
            _ => $"未知种类（{(uint)card.Kind.Value}）",
        };

        _displayText = $"{zoneName} {index + 1}\n{title}\n{detail}";
        TooltipText = _identityHidden
            ? string.Empty
            : CardPresentation.FormatCompact(card);
        SetSelectable(selectable && !_identityHidden);
        SetSelected(false);
        SetAffordance(SnapshotAffordance.None);
    }

    public void ShowPublicCard(
        HotseatPublicCardView card,
        string zoneName,
        int index)
    {
        ZoneName = zoneName;
        SlotIndex = index;
        _hasCard = true;
        _identityHidden = !card.HasKnownIdentity;
        _knownInstanceId = _identityHidden ? null : card.InstanceId;

        string title = _identityHidden ? "伏策（背面）" : card.Name;
        string detail = card.Kind switch
        {
            CardKind.Unit => $"{card.CurrentAttack} / {card.CurrentHealth}",
            CardKind.Relic => $"倒计时 {card.Countdown}",
            CardKind.Trap => _identityHidden ? "身份已隐藏" : "伏策",
            CardKind.Spell => $"费用 {card.Cost}",
            null => "身份已隐藏",
            _ => $"未知种类（{(uint)card.Kind.Value}）",
        };

        _displayText = $"{zoneName} {index + 1}\n{title}\n{detail}";
        TooltipText = string.Empty;
        SetSelectable(false);
        SetSelected(false);
        SetDirectInteraction(draggable: false, dropTarget: false);
        SetAffordance(SnapshotAffordance.None);
    }

    public void SetSelectable(bool selectable, string? actionHint = null)
    {
        Disabled = !selectable;
        FocusMode = selectable ? FocusModeEnum.All : FocusModeEnum.None;
        MouseDefaultCursorShape = selectable
            ? CursorShape.PointingHand
            : CursorShape.Arrow;

        if (selectable && !_identityHidden && !string.IsNullOrWhiteSpace(actionHint))
        {
            TooltipText = string.IsNullOrWhiteSpace(TooltipText)
                ? actionHint
                : $"{TooltipText}\n{actionHint}";
        }
    }

    public void SetSelected(bool selected)
    {
        SetPressedNoSignal(selected);
        Modulate = selected
            ? new Color(0.62f, 1.0f, 0.91f, 1.0f)
            : Colors.White;
    }

    public void SetDirectInteraction(bool draggable, bool dropTarget)
    {
        _dragEnabled = draggable && !_identityHidden;
        _dropEnabled = dropTarget;
    }

    public void SetAffordance(SnapshotAffordance affordance)
    {
        _affordance = affordance;
        string marker = affordance switch
        {
            SnapshotAffordance.Source => "▶ ",
            SnapshotAffordance.Action => "◆ ",
            SnapshotAffordance.Target => "◎ ",
            SnapshotAffordance.Slot => "＋ ",
            SnapshotAffordance.Donor => "◇ ",
            SnapshotAffordance.Selected => "✓ ",
            _ => string.Empty,
        };
        _label.Text = marker + _displayText;

        Color outline = affordance switch
        {
            SnapshotAffordance.Target => new Color(1.0f, 0.43f, 0.36f),
            SnapshotAffordance.Donor => new Color(0.98f, 0.69f, 0.28f),
            SnapshotAffordance.Slot => new Color(0.42f, 0.86f, 0.76f),
            SnapshotAffordance.Selected => new Color(0.54f, 1.0f, 0.77f),
            SnapshotAffordance.Source or SnapshotAffordance.Action => new Color(0.46f, 0.75f, 1.0f),
            _ => new Color(0.24f, 0.33f, 0.43f),
        };
        var style = new StyleBoxFlat
        {
            BgColor = affordance == SnapshotAffordance.None
                ? new Color(0.05f, 0.09f, 0.14f, 0.74f)
                : new Color(outline.R * 0.16f, outline.G * 0.16f, outline.B * 0.16f, 0.96f),
            BorderColor = outline,
            BorderWidthLeft = affordance == SnapshotAffordance.None ? 1 : 3,
            BorderWidthTop = affordance == SnapshotAffordance.None ? 1 : 3,
            BorderWidthRight = affordance == SnapshotAffordance.None ? 1 : 3,
            BorderWidthBottom = affordance == SnapshotAffordance.None ? 1 : 3,
            CornerRadiusTopLeft = 7,
            CornerRadiusTopRight = 7,
            CornerRadiusBottomLeft = 7,
            CornerRadiusBottomRight = 7,
        };
        AddThemeStyleboxOverride("normal", style);
        AddThemeStyleboxOverride("disabled", style);
    }

    public void ClearSensitive()
    {
        ZoneName = string.Empty;
        SlotIndex = -1;
        _knownInstanceId = null;
        _hasCard = false;
        _identityHidden = false;
        _displayText = string.Empty;
        _label.Text = string.Empty;
        TooltipText = string.Empty;
        _dragEnabled = false;
        _dropEnabled = false;
        _affordance = SnapshotAffordance.None;
        foreach (StringName key in GetMetaList())
        {
            RemoveMeta(key);
        }
        SetSelectable(false);
        SetSelected(false);
    }

    internal void ArmPrivacySentinelForSmoke(string sentinel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sentinel);
        _displayText = sentinel;
        _label.Text = sentinel;
        TooltipText = sentinel;
        SetMeta("scgs_ci_private_sentinel", sentinel);
        _dragEnabled = true;
        _dropEnabled = true;
        _affordance = SnapshotAffordance.Target;
        Disabled = false;
    }

    internal Variant BeginDragForSmoke()
    {
        _smokeDrag = true;
        try
        {
            return _GetDragData(Vector2.Zero);
        }
        finally
        {
            _smokeDrag = false;
        }
    }

    internal void DropForSmoke(Variant payload) => _DropData(Vector2.Zero, payload);

    private void OnPressed()
    {
        if (Disabled)
        {
            return;
        }

        Activated?.Invoke(this);
    }
}

public enum SnapshotAffordance
{
    None,
    Source,
    Action,
    Target,
    Slot,
    Donor,
    Selected,
}
