using Godot;
using Scgs.Client;

namespace Scgs.GodotClient.UI;

public sealed partial class SnapshotSlot : PanelContainer
{
    private Label _label = null!;

    public override void _Ready()
    {
        _label = GetNode<Label>("%SlotLabel");
    }

    public void ShowEmpty(string zoneName, int index)
    {
        _label.Text = $"{zoneName} {index + 1}\n— 空 —";
        TooltipText = string.Empty;
    }

    public void ShowCard(CardView card, string zoneName, int index)
    {
        string title = card.FaceDown ? "伏策（背面）" : card.Name;
        string detail = card.Kind switch
        {
            CardKind.Unit => $"{card.CurrentAttack} / {card.CurrentHealth}",
            CardKind.Relic => $"倒计时 {card.Countdown}",
            CardKind.Trap => card.FaceDown ? "身份已隐藏" : "伏策",
            CardKind.Spell => $"费用 {card.Cost}",
            null => "身份已隐藏",
            _ => string.Empty,
        };

        _label.Text = $"{zoneName} {index + 1}\n{title}\n{detail}";
        TooltipText = card.FaceDown
            ? "对方背面伏策：引擎未向当前观看者提供卡牌身份。"
            : $"费用 {card.Cost} · {card.Keywords}";
    }
}
