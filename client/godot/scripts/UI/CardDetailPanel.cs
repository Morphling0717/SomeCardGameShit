// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Presentation;
using Scgs.GodotClient.Visuals;

namespace Scgs.GodotClient.UI;

public sealed partial class CardDetailPanel : PanelContainer
{
    private const float CompactWidth = 94.0f;
    private const float CompactHeight = 58.0f;

    private Label _title = null!;
    private RichTextLabel _rules = null!;
    private TextureRect _artwork = null!;
    private Control _detailBody = null!;
    private Button _collapseButton = null!;
    private readonly ICardVisualCatalog _visualCatalog = CardVisualCatalog.Shared;
    private Rect2 _expandedRect;
    private bool _isCompact;

    internal bool HasSensitiveContentForSmoke =>
        !string.IsNullOrEmpty(_title.Text) || !string.IsNullOrEmpty(_rules.Text) ||
        _artwork is not null && _artwork.Texture is not null;

    internal bool ShowsKnownCardForSmoke(CardView card) =>
        card.DefinitionId.HasValue &&
        _artwork.Texture is not null &&
        _artwork.Texture.ResourcePath.EndsWith(
            $"/{card.DefinitionId.Value}.png",
            StringComparison.Ordinal) &&
        _rules.Text.Contains(card.Name, StringComparison.Ordinal);

    public override void _Ready()
    {
        _title = GetNode<Label>("%CardDetailTitle");
        _rules = GetNode<RichTextLabel>("%CardDetailRules");
        _artwork = GetNode<TextureRect>("%CardDetailArtwork");
        _detailBody = GetNode<Control>("%CardDetailBody");
        _collapseButton = GetNode<Button>("%CardDetailCollapseButton");
        _collapseButton.Pressed += ToggleCollapsed;
        _expandedRect = new Rect2(Position, Size);
        ShowPlaceholder();
    }

    public void ShowCard(CardView card, string heading = "卡牌详情")
    {
        SetCompact(false);
        _title.Text = heading;
        _rules.Text = CardPresentation.FormatRules(card);
        _artwork.Texture = card.DefinitionId.HasValue
            ? _visualCatalog.LoadArtwork(card.DefinitionId.Value)
            : _visualCatalog.FallbackFront;
    }

    public void ShowHiddenCard()
    {
        SetCompact(false);
        _title.Text = "隐藏牌";
        _rules.Text = "当前观看者没有收到这张牌的身份。不会显示名称、编号或规则。";
        _artwork.Texture = _visualCatalog.CardBack;
    }

    public void ShowPlaceholder()
    {
        _title.Text = "卡牌";
        _rules.Text = string.Empty;
        _artwork.Texture = null;
        TooltipText = "悬停卡牌查看详情；右键可固定。";
        SetCompact(true);
    }

    public void ClearSensitive()
    {
        _title.Text = string.Empty;
        _rules.Text = string.Empty;
        _artwork.Texture = null;
        TooltipText = string.Empty;
        SetCompact(true);
    }

    public void SetExpandedRect(Rect2 rect)
    {
        if (rect.Size.X < 248.0f || rect.Size.Y < 360.0f ||
            !float.IsFinite(rect.Position.X) || !float.IsFinite(rect.Position.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(rect));
        }

        _expandedRect = rect;
        ApplyFloatingRect();
    }

    public void SetCompact(bool compact)
    {
        _isCompact = compact;
        _detailBody.Visible = !compact;
        _collapseButton.Visible = !compact;
        _collapseButton.Text = compact ? "展开" : "收起";
        CustomMinimumSize = compact
            ? new Vector2(CompactWidth, CompactHeight)
            : new Vector2(Mathf.Max(_expandedRect.Size.X, 248.0f), 360.0f);
        ApplyFloatingRect();
    }

    private void ToggleCollapsed()
    {
        SetCompact(!_isCompact);
    }

    private void ApplyFloatingRect()
    {
        if (GetParent() is Container || _expandedRect.Size.X <= 0.0f)
        {
            return;
        }

        Rect2 target = _isCompact
            ? new Rect2(_expandedRect.Position, new Vector2(CompactWidth, CompactHeight))
            : _expandedRect;
        Position = target.Position;
        Size = target.Size;
    }
}
