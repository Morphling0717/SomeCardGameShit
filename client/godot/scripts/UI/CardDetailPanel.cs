// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;

namespace Scgs.GodotClient.UI;

public sealed partial class CardDetailPanel : PanelContainer
{
    private Label _title = null!;
    private RichTextLabel _rules = null!;

    public override void _Ready()
    {
        _title = GetNode<Label>("%CardDetailTitle");
        _rules = GetNode<RichTextLabel>("%CardDetailRules");
        ShowPlaceholder();
    }

    public void ShowCard(CardView card, string heading = "卡牌详情")
    {
        _title.Text = heading;
        _rules.Text = CardPresentation.FormatRules(card);
    }

    public void ShowHiddenCard()
    {
        _title.Text = "隐藏牌";
        _rules.Text = "当前观看者没有收到这张牌的身份。不会显示名称、编号或规则。";
    }

    public void ShowPlaceholder()
    {
        _title.Text = "卡牌详情";
        _rules.Text = "选择己方手牌、单位、战备牌或已知伏策以查看规则。";
    }

    public void ClearSensitive()
    {
        _title.Text = string.Empty;
        _rules.Text = string.Empty;
    }
}
