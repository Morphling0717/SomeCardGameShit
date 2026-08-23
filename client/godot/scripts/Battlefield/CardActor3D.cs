// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.Hotseat;
using System.Globalization;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class CardActor3D : Area3D, IBattlefieldPickTarget
{
    public const uint PickCollisionLayer = 1U << 9;

    private static readonly Color KnownColor = new("2d7876");
    private static readonly Color HiddenColor = new("172943");
    private static readonly Color LegalColor = new("44c6a5");
    private static readonly Color DestinationColor = new("f0b84d");
    private static readonly Color SelectedColor = new("f4775f");
    private static readonly Color NeutralColor = new("33495f");

    private MeshInstance3D _mesh = null!;
    private MeshInstance3D _outlineMesh = null!;
    private Label3D _faceLabel = null!;
    private Label3D _stateLabel = null!;
    private CollisionShape3D _collision = null!;
    private StandardMaterial3D _knownMaterial = null!;
    private StandardMaterial3D _hiddenMaterial = null!;
    private StandardMaterial3D _neutralMaterial = null!;
    private StandardMaterial3D _legalMaterial = null!;
    private StandardMaterial3D _destinationMaterial = null!;
    private StandardMaterial3D _selectedMaterial = null!;
    private StandardMaterial3D _legalOutlineMaterial = null!;
    private StandardMaterial3D _destinationOutlineMaterial = null!;
    private StandardMaterial3D _selectedOutlineMaterial = null!;
    private Transform3D _restTransform = Transform3D.Identity;
    private BattlefieldSurfaceRef? _boundSurface;
    private BattlefieldHighlightKind _highlight;
    private bool _faceDown;
    private bool _pointerHovered;

    public BattlefieldSurfaceRef? Surface { get; private set; }

    public BattlefieldCardPresentation? CardPresentation { get; private set; }

    public Vector3 WorldAnchor => GlobalPosition + new Vector3(0.0f, 0.18f, 0.0f);

    public bool CanActivate { get; private set; }

    public bool CollisionEnabled => CollisionLayer != 0 && !_collision.Disabled;

    public string DisplayText => _faceLabel?.Text ?? string.Empty;

    public string StateText => _stateLabel?.Text ?? string.Empty;

    public bool OutlineVisible => _outlineMesh?.Visible == true;

    public bool HasTripleAffordance =>
        Visible && CanActivate && _highlight != BattlefieldHighlightKind.None &&
        OutlineVisible && !string.IsNullOrWhiteSpace(StateText);

    public override void _Ready()
    {
        EnsureBuilt();
        ClearSensitive();
    }

    public void BindPrivate(
        CardView card,
        BattlefieldSurfaceRef? surface,
        Transform3D transform)
    {
        ArgumentNullException.ThrowIfNull(card);
        bool known = card.InstanceId.HasValue && card.DefinitionId.HasValue;
        var presentation = new BattlefieldCardPresentation(
            card.InstanceId,
            card.DefinitionId,
            known ? card.Name : string.Empty,
            known ? card.Kind : null,
            card.Controller,
            card.Zone,
            known ? card.Cost : 0,
            known ? card.CurrentAttack : 0,
            known ? card.CurrentHealth : 0,
            known ? card.MaximumHealth : 0,
            known ? card.Countdown : 0,
            card.FaceDown,
            known);
        Bind(presentation, surface, transform, showIdentityOnBoard: known && !card.FaceDown);
    }

    public void BindPublic(
        HotseatPublicCardView card,
        Transform3D transform)
    {
        ArgumentNullException.ThrowIfNull(card);
        bool known = card.HasKnownIdentity && !card.FaceDown;
        var presentation = new BattlefieldCardPresentation(
            null,
            known ? card.DefinitionId : null,
            known ? card.Name : string.Empty,
            known ? card.Kind : null,
            card.Controller,
            card.Zone,
            known ? card.Cost : 0,
            known ? card.CurrentAttack : 0,
            known ? card.CurrentHealth : 0,
            known ? card.MaximumHealth : 0,
            known ? card.Countdown : 0,
            card.FaceDown,
            known);
        Bind(presentation, surface: null, transform, showIdentityOnBoard: known);
        CollisionLayer = 0;
    }

    public void BindHidden(
        PlayerId controller,
        Zone zone,
        Transform3D transform,
        BattlefieldSurfaceRef? surface = null)
    {
        var presentation = new BattlefieldCardPresentation(
            null,
            null,
            string.Empty,
            null,
            controller,
            zone,
            0,
            0,
            0,
            0,
            0,
            true,
            false);
        Bind(presentation, surface, transform, showIdentityOnBoard: false);
    }

    public void BindPile(
        PlayerId controller,
        Zone zone,
        string title,
        ulong count,
        Transform3D transform,
        bool hidden,
        BattlefieldSurfaceRef? surface = null)
    {
        var presentation = new BattlefieldCardPresentation(
            null,
            null,
            string.Empty,
            null,
            controller,
            zone,
            0,
            0,
            0,
            0,
            0,
            hidden,
            false);
        Bind(presentation, surface, transform, showIdentityOnBoard: false);
        _faceLabel.Text = $"{title}\n{count}";
    }

    public void SetHighlight(BattlefieldHighlightKind highlight)
    {
        EnsureBuilt();
        _highlight = highlight;
        CanActivate = Surface.HasValue && highlight != BattlefieldHighlightKind.None;
        _stateLabel.Text = highlight switch
        {
            BattlefieldHighlightKind.Legal => "● 可选",
            BattlefieldHighlightKind.Selected => "◆ 已选",
            BattlefieldHighlightKind.Destination => "◎ 目标",
            _ => string.Empty,
        };
        UpdateMaterial();
    }

    public void SetPickEnabled(bool enabled)
    {
        EnsureBuilt();
        bool pickable = enabled && Surface.HasValue;
        _collision.Disabled = !pickable;
        CollisionLayer = pickable ? PickCollisionLayer : 0;
    }

    public void SetUtilityInteractive(string label)
    {
        EnsureBuilt();
        if (!Visible || !Surface.HasValue)
        {
            throw new InvalidOperationException("A utility card must expose a bound surface.");
        }

        CanActivate = true;
        _stateLabel.Text = label;
        SetPickEnabled(enabled: true);
    }

    public void OverrideInteractionSurface(BattlefieldSurfaceRef surface)
    {
        EnsureBuilt();
        if (!Visible || !_boundSurface.HasValue)
        {
            throw new InvalidOperationException("Only a bound private card can override its surface.");
        }

        Surface = surface;
        SetPickEnabled(enabled: true);
    }

    public void RestoreBoundSurface()
    {
        EnsureBuilt();
        Surface = _boundSurface;
        SetPickEnabled(enabled: _boundSurface.HasValue);
    }

    public void SetPointerHovered(bool hovered)
    {
        EnsureBuilt();
        if (_pointerHovered == hovered || !Visible)
        {
            return;
        }

        _pointerHovered = hovered;
        Transform3D transform = _restTransform;
        if (hovered)
        {
            transform.Origin += Vector3.Up * 0.2f;
        }

        Transform = transform;
    }

    public void ClearSensitive()
    {
        EnsureBuilt();
        Surface = null;
        _boundSurface = null;
        CardPresentation = null;
        CanActivate = false;
        _highlight = BattlefieldHighlightKind.None;
        _faceDown = false;
        _pointerHovered = false;
        _restTransform = Transform3D.Identity;
        Transform = Transform3D.Identity;
        _faceLabel.Text = string.Empty;
        _stateLabel.Text = string.Empty;
        _mesh.MaterialOverride = _neutralMaterial;
        _outlineMesh.Visible = false;
        _outlineMesh.MaterialOverride = _legalOutlineMaterial;
        ClearMaterialResourceNames();
        _collision.Disabled = true;
        CollisionLayer = 0;
        Visible = false;
        ScrubMetadata(this);
    }

    public int CountForbiddenToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return 0;
        }

        int count = 0;
        count += ContainsToken(DisplayText, token) ? 1 : 0;
        count += ContainsToken(StateText, token) ? 1 : 0;
        count += ContainsToken(CardPresentation?.Name, token) ? 1 : 0;
        count += ContainsToken(_knownMaterial.ResourceName, token) ? 1 : 0;
        count += ContainsToken(_hiddenMaterial.ResourceName, token) ? 1 : 0;
        count += ContainsToken(_neutralMaterial.ResourceName, token) ? 1 : 0;
        count += ContainsToken(_legalMaterial.ResourceName, token) ? 1 : 0;
        count += ContainsToken(_destinationMaterial.ResourceName, token) ? 1 : 0;
        count += ContainsToken(_selectedMaterial.ResourceName, token) ? 1 : 0;
        count += ContainsToken(_legalOutlineMaterial.ResourceName, token) ? 1 : 0;
        count += ContainsToken(_destinationOutlineMaterial.ResourceName, token) ? 1 : 0;
        count += ContainsToken(_selectedOutlineMaterial.ResourceName, token) ? 1 : 0;
        count += CountMetadataToken(this, token);
        return count;
    }

    public void CiArmPrivacySentinel(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        EnsureBuilt();
        if (!Visible || CardPresentation is null)
        {
            throw new InvalidOperationException("A visible card must be bound before arming privacy data.");
        }

        _faceLabel.Text = token;
        CardPresentation = CardPresentation with { Name = token };
        SetMeta("ci_private_sentinel", token);
        _mesh.SetMeta("ci_private_sentinel", token);
        _knownMaterial.ResourceName = token;
    }

    private void Bind(
        BattlefieldCardPresentation presentation,
        BattlefieldSurfaceRef? surface,
        Transform3D transform,
        bool showIdentityOnBoard)
    {
        EnsureBuilt();
        Surface = surface;
        _boundSurface = surface;
        CardPresentation = presentation;
        _faceDown = presentation.FaceDown || !showIdentityOnBoard;
        _restTransform = transform;
        Transform = transform;
        _pointerHovered = false;
        _highlight = BattlefieldHighlightKind.None;
        CanActivate = false;
        _faceLabel.Text = showIdentityOnBoard
            ? FormatKnownCard(presentation)
            : "牌背";
        _stateLabel.Text = string.Empty;
        _outlineMesh.Visible = false;
        _collision.Disabled = !surface.HasValue;
        CollisionLayer = surface.HasValue ? PickCollisionLayer : 0;
        Visible = true;
        UpdateMaterial();
    }

    private static string FormatKnownCard(BattlefieldCardPresentation card)
    {
        string name = CompactCardName(card.Name);
        return card.Kind switch
        {
            CardKind.Unit => $"{card.Cost}费  {card.Attack}/{card.Health}\n{name}",
            CardKind.Relic or CardKind.Trap => card.Countdown > 0
                ? $"{card.Cost}费  倒{card.Countdown}\n{name}"
                : $"{card.Cost}费\n{name}",
            _ => $"{card.Cost}费\n{name}",
        };
    }

    private static string CompactCardName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "—";
        }

        int[] elements = StringInfo.ParseCombiningCharacters(name);
        if (elements.Length <= 3)
        {
            return name;
        }

        int split = elements[3];
        int keptElements = Math.Min(elements.Length, 6);
        int keptLength = keptElements < elements.Length ? elements[keptElements] : name.Length;
        string suffix = keptElements < elements.Length ? "…" : string.Empty;
        return $"{name[..split]}\n{name[split..keptLength]}{suffix}";
    }

    private void UpdateMaterial()
    {
        _mesh.MaterialOverride = _highlight switch
        {
            BattlefieldHighlightKind.Legal => _legalMaterial,
            BattlefieldHighlightKind.Selected => _selectedMaterial,
            BattlefieldHighlightKind.Destination => _destinationMaterial,
            _ => _faceDown ? _hiddenMaterial : _knownMaterial,
        };
        _outlineMesh.MaterialOverride = _highlight switch
        {
            BattlefieldHighlightKind.Legal => _legalOutlineMaterial,
            BattlefieldHighlightKind.Selected => _selectedOutlineMaterial,
            BattlefieldHighlightKind.Destination => _destinationOutlineMaterial,
            _ => _legalOutlineMaterial,
        };
        _outlineMesh.Visible = _highlight != BattlefieldHighlightKind.None;
    }

    private void ClearMaterialResourceNames()
    {
        _knownMaterial.ResourceName = string.Empty;
        _hiddenMaterial.ResourceName = string.Empty;
        _neutralMaterial.ResourceName = string.Empty;
        _legalMaterial.ResourceName = string.Empty;
        _destinationMaterial.ResourceName = string.Empty;
        _selectedMaterial.ResourceName = string.Empty;
        _legalOutlineMaterial.ResourceName = string.Empty;
        _destinationOutlineMaterial.ResourceName = string.Empty;
        _selectedOutlineMaterial.ResourceName = string.Empty;
    }

    private void EnsureBuilt()
    {
        if (_mesh is not null)
        {
            return;
        }

        _knownMaterial = CreateMaterial(KnownColor);
        _hiddenMaterial = CreateMaterial(HiddenColor);
        _neutralMaterial = CreateMaterial(NeutralColor);
        _legalMaterial = CreateMaterial(LegalColor, emission: true);
        _destinationMaterial = CreateMaterial(DestinationColor, emission: true);
        _selectedMaterial = CreateMaterial(SelectedColor, emission: true);
        _legalOutlineMaterial = CreateOutlineMaterial(LegalColor);
        _destinationOutlineMaterial = CreateOutlineMaterial(DestinationColor);
        _selectedOutlineMaterial = CreateOutlineMaterial(SelectedColor);

        _outlineMesh = new MeshInstance3D
        {
            Name = "AffordanceOutline",
            Mesh = new BoxMesh { Size = new Vector3(1.78f, 0.03f, 2.38f) },
            Position = new Vector3(0.0f, -0.055f, 0.0f),
            MaterialOverride = _legalOutlineMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_outlineMesh);

        _mesh = new MeshInstance3D
        {
            Name = "CardMesh",
            Mesh = new BoxMesh { Size = new Vector3(1.58f, 0.09f, 2.18f) },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        AddChild(_mesh);

        _collision = new CollisionShape3D
        {
            Name = "PickShape",
            Shape = new BoxShape3D { Size = new Vector3(1.66f, 0.28f, 2.26f) },
            Position = new Vector3(0.0f, 0.1f, 0.0f),
        };
        AddChild(_collision);

        _faceLabel = CreateTopLabel("FaceLabel", 44, new Vector3(0.0f, 0.065f, 0.0f));
        _faceLabel.Width = 134.0f;
        AddChild(_faceLabel);

        _stateLabel = CreateTopLabel("StateLabel", 36, new Vector3(0.0f, 0.075f, -0.78f));
        _stateLabel.Modulate = new Color(1.0f, 0.94f, 0.63f, 1.0f);
        AddChild(_stateLabel);

        CollisionMask = 0;
        InputRayPickable = true;
        Monitoring = false;
        Monitorable = false;
    }

    private static Label3D CreateTopLabel(
        string name,
        int fontSize,
        Vector3 position) => new()
        {
            Name = name,
            Font = GD.Load<Font>("res://assets/fonts/NotoSansCJKsc-Regular.otf"),
            FontSize = fontSize,
            PixelSize = 0.0115f,
            OutlineSize = 9,
            Modulate = Colors.White,
            Position = position,
            RotationDegrees = new Vector3(-90.0f, 0.0f, 0.0f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            DoubleSided = true,
            NoDepthTest = false,
        };

    private static StandardMaterial3D CreateMaterial(Color color, bool emission = false) => new()
    {
        AlbedoColor = color,
        Metallic = 0.08f,
        Roughness = 0.68f,
        EmissionEnabled = emission,
        Emission = emission ? color * 0.42f : Colors.Black,
    };

    private static StandardMaterial3D CreateOutlineMaterial(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = 0.54f,
        EmissionEnabled = true,
        Emission = color * 0.9f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static bool ContainsToken(string? value, string token) =>
        value?.Contains(token, StringComparison.Ordinal) == true;

    private static void ScrubMetadata(Node node)
    {
        foreach (StringName key in node.GetMetaList())
        {
            node.RemoveMeta(key);
        }

        foreach (Node child in node.GetChildren())
        {
            ScrubMetadata(child);
        }
    }

    private static int CountMetadataToken(Node node, string token)
    {
        int count = 0;
        foreach (StringName key in node.GetMetaList())
        {
            string keyText = key.ToString();
            string valueText = node.GetMeta(key).ToString();
            count += ContainsToken(keyText, token) ? 1 : 0;
            count += ContainsToken(valueText, token) ? 1 : 0;
        }

        foreach (Node child in node.GetChildren())
        {
            count += CountMetadataToken(child, token);
        }

        return count;
    }
}
