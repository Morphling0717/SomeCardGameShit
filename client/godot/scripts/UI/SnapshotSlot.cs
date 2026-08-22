using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;

namespace Scgs.GodotClient.UI;

public sealed partial class SnapshotSlot : Button
{
    private Label _label = null!;
    private ulong? _knownInstanceId;
    private bool _hasCard;
    private bool _identityHidden;

    public event Action<SnapshotSlot>? Activated;

    public string ZoneName { get; private set; } = string.Empty;

    public int SlotIndex { get; private set; } = -1;

    public bool HasCard => _hasCard;

    public bool HasKnownIdentity => _hasCard && !_identityHidden && _knownInstanceId.HasValue;

    public ulong? KnownInstanceId => HasKnownIdentity ? _knownInstanceId : null;

    public bool IsSelected => ButtonPressed;

    public override void _Ready()
    {
        _label = GetNode<Label>("%SlotLabel");
        ToggleMode = true;
        Disabled = true;
        FocusMode = FocusModeEnum.None;
        Pressed += OnPressed;
    }

    public void ShowEmpty(string zoneName, int index, bool selectable = false)
    {
        ZoneName = zoneName;
        SlotIndex = index;
        _hasCard = false;
        _identityHidden = false;
        _knownInstanceId = null;
        _label.Text = $"{zoneName} {index + 1}\n— 空 —";
        TooltipText = string.Empty;
        SetSelectable(selectable);
        SetSelected(false);
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

        _label.Text = $"{zoneName} {index + 1}\n{title}\n{detail}";
        TooltipText = _identityHidden
            ? string.Empty
            : CardPresentation.FormatCompact(card);
        SetSelectable(selectable && !_identityHidden);
        SetSelected(false);
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

    public void ClearSensitive()
    {
        ZoneName = string.Empty;
        SlotIndex = -1;
        _knownInstanceId = null;
        _hasCard = false;
        _identityHidden = false;
        _label.Text = string.Empty;
        TooltipText = string.Empty;
        SetSelectable(false);
        SetSelected(false);
    }

    private void OnPressed()
    {
        if (Disabled)
        {
            return;
        }

        Activated?.Invoke(this);
    }
}
