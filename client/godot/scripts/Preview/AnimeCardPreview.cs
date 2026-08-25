// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.Preview;

internal sealed partial class AnimeCardPreview : Control
{
    private string _designId = string.Empty;
    private string _displayName = string.Empty;
    private AnimeCardKind _kind;
    private AnimeFaction _faction;
    private int _cost;
    private int? _attack;
    private int? _health;
    private int? _countdown;
    private bool _hidden;
    private bool _evolved;
    private Texture2D? _art;

    internal string DesignId => _designId;
    internal bool ShowsIdentity => !_hidden;
    internal bool UsesExpectedRaster => _art is not null;
    internal bool IsHidden => _hidden;
    internal AnimeCardKind Kind => _kind;
    internal bool IsOwnHandCard => Name.ToString().StartsWith("NearHand", StringComparison.Ordinal);
    internal string TypeMarkerGlyph => _kind switch
    {
        AnimeCardKind.Follower => "随",
        AnimeCardKind.Spell => "法",
        AnimeCardKind.Amulet => "护",
        AnimeCardKind.Trap => "伏",
        AnimeCardKind.Field => "场",
        _ => "?",
    };
    internal string TypeMarkerShape => _kind switch
    {
        AnimeCardKind.Follower => "shield",
        AnimeCardKind.Spell => "star",
        AnimeCardKind.Amulet => "ring",
        AnimeCardKind.Trap => "inverted_triangle",
        AnimeCardKind.Field => "gate",
        _ => "unknown",
    };
    internal int BadgeFontPixelSize => Math.Max(13, (int)MathF.Round(20.0f * CardScale));
    internal Rect2 VisualScreenRect => TransformLocalRect(new Rect2(Vector2.Zero, Size));
    internal Rect2 CostBadgeScreenRect => TransformLocalRect(BadgeLocalRect(CostBadgeCenter, 14.0f * CardScale));
    internal Rect2? AttackBadgeScreenRect => !_hidden && _attack.HasValue
        ? TransformLocalRect(BadgeLocalRect(AttackBadgeCenter, 13.0f * CardScale))
        : null;
    internal Rect2? HealthBadgeScreenRect => !_hidden && _health.HasValue
        ? TransformLocalRect(BadgeLocalRect(RightStatBadgeCenter, 13.0f * CardScale))
        : null;
    internal Rect2? CountdownBadgeScreenRect => !_hidden && !_health.HasValue && _countdown.HasValue
        ? TransformLocalRect(BadgeLocalRect(RightStatBadgeCenter, 13.0f * CardScale))
        : null;
    internal Rect2 TypeMarkerScreenRect => TransformLocalRect(TypeMarkerLocalRect);

    private float CardScale => MathF.Min(Size.X / 120.0f, Size.Y / 180.0f);
    private Vector2 CostBadgeCenter => new(17.0f * CardScale, 18.0f * CardScale);
    private Vector2 AttackBadgeCenter => new(
        IsOwnHandCard ? Size.X * 0.28f : 17.0f * CardScale,
        Size.Y - (17.0f * CardScale));
    private Vector2 RightStatBadgeCenter => new(
        IsOwnHandCard ? Size.X * 0.65f : Size.X - (17.0f * CardScale),
        Size.Y - (17.0f * CardScale));
    private float TypeMarkerRadius => MathF.Max(12.0f, 16.0f * CardScale);
    private Vector2 TypeMarkerCenter => new(Size.X * 0.62f, TypeMarkerRadius + (5.0f * CardScale));
    private Rect2 TypeMarkerLocalRect => new(
        TypeMarkerCenter - (Vector2.One * TypeMarkerRadius),
        Vector2.One * TypeMarkerRadius * 2.0f);

    internal void Configure(
        string designId,
        string displayName,
        AnimeCardKind kind,
        AnimeFaction faction,
        int cost,
        int? attack = null,
        int? health = null,
        int? countdown = null,
        bool hidden = false,
        bool evolved = false)
    {
        _designId = designId;
        _displayName = displayName;
        _kind = kind;
        _faction = faction;
        _cost = cost;
        _attack = attack;
        _health = health;
        _countdown = countdown;
        _hidden = hidden;
        _evolved = evolved;
        string artId = evolved ? $"{designId}-EVOLVED" : designId;
        _art = hidden
            ? AnimeVisualAssetCatalog.TryLoad(AnimeVisualAssetCatalog.CardBack)
            : AnimeVisualAssetCatalog.Card(artId);
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 bounds = new(Vector2.Zero, Size);
        if (bounds.Size.X < 8.0f || bounds.Size.Y < 12.0f)
        {
            return;
        }

        float scale = MathF.Min(bounds.Size.X / 120.0f, bounds.Size.Y / 180.0f);
        float outer = MathF.Max(2.0f, 4.0f * scale);
        float inner = MathF.Max(2.0f, 5.0f * scale);
        Color faction = AnimeVisualTheme.FactionColor(_faction);
        Color baseColor = _hidden
            ? AnimeVisualTheme.DeepIndigo.Darkened(0.18f)
            : faction.Darkened(0.64f);

        StyleBoxFlat shadow = AnimeVisualTheme.Panel(AnimeVisualTheme.Ink, 0.62f, (int)(13 * scale), 0);
        shadow.ShadowSize = (int)MathF.Max(4.0f, 10.0f * scale);
        DrawStyleBox(shadow, bounds.Grow(-1.0f));

        StyleBoxFlat frame = AnimeVisualTheme.Panel(baseColor, 0.98f, (int)(12 * scale), (int)MathF.Max(1.0f, 2.0f * scale));
        frame.BorderColor = _evolved
            ? new Color(AnimeVisualTheme.PaleGold, 1.0f)
            : new Color(faction.Lightened(0.32f), 0.95f);
        frame.ShadowSize = 0;
        DrawStyleBox(frame, bounds.Grow(-outer * 0.35f));

        Rect2 artRect = new(
            new Vector2(inner + outer, inner + outer),
            new Vector2(
                bounds.Size.X - ((inner + outer) * 2.0f),
                bounds.Size.Y - (39.0f * scale) - ((inner + outer) * 1.65f)));
        artRect.Size = new Vector2(
            MathF.Max(4.0f, artRect.Size.X),
            MathF.Max(8.0f, artRect.Size.Y));
        DrawRect(artRect, new Color(baseColor.Lightened(0.18f), 1.0f));
        if (_art is not null)
        {
            DrawTextureRect(_art, artRect, tile: false);
        }
        else
        {
            DrawFallbackArtwork(artRect, faction, scale);
        }

        DrawOrnaments(bounds, artRect, faction, scale);
        if (_hidden)
        {
            DrawHiddenSigil(artRect, scale);
            return;
        }

        DrawTypeMarker(scale, faction);

        float stripHeight = 29.0f * scale;
        Rect2 nameStrip = new(
            new Vector2(inner, bounds.Size.Y - stripHeight - inner),
            new Vector2(bounds.Size.X - (inner * 2.0f), stripHeight));
        DrawRect(nameStrip, new Color(AnimeVisualTheme.Ink, 0.91f));
        DrawLine(
            nameStrip.Position,
            nameStrip.Position + new Vector2(nameStrip.Size.X, 0.0f),
            new Color(AnimeVisualTheme.OldGold, 0.85f),
            MathF.Max(1.0f, scale));
        int nameSize = Math.Max(10, (int)MathF.Round(14.0f * scale));
        DrawString(
            AnimeVisualTheme.DisplayFont,
            nameStrip.Position + new Vector2(4.0f * scale, 19.0f * scale),
            Shorten(_displayName, scale < 0.8f ? 7 : 10),
            HorizontalAlignment.Center,
            nameStrip.Size.X - (8.0f * scale),
            nameSize,
            AnimeVisualTheme.MoonWhite);

        DrawBadge(
            CostBadgeCenter,
            14.0f * scale,
            _cost.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AnimeVisualTheme.OathBlue,
            scale);
        if (_attack.HasValue)
        {
            DrawBadge(
                AttackBadgeCenter,
                13.0f * scale,
                _attack.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AnimeVisualTheme.PactCrimson,
                scale);
        }
        if (_health.HasValue)
        {
            DrawBadge(
                RightStatBadgeCenter,
                13.0f * scale,
                _health.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AnimeVisualTheme.Positive,
                scale);
        }
        else if (_countdown.HasValue)
        {
            DrawBadge(
                RightStatBadgeCenter,
                13.0f * scale,
                _countdown.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                AnimeVisualTheme.PaleGold,
                scale);
        }
    }

    private void DrawFallbackArtwork(Rect2 rect, Color faction, float scale)
    {
        const int bands = 12;
        for (int index = 0; index < bands; index++)
        {
            float t = index / (float)bands;
            Rect2 band = new(
                rect.Position + new Vector2(0.0f, rect.Size.Y * t),
                new Vector2(rect.Size.X, (rect.Size.Y / bands) + 1.0f));
            Color color = faction.Darkened(0.56f - (t * 0.24f));
            DrawRect(band, color);
        }
        Vector2 center = rect.GetCenter() + new Vector2(0.0f, rect.Size.Y * 0.08f);
        DrawCircle(center - new Vector2(0.0f, rect.Size.Y * 0.24f), 13.0f * scale, new Color(AnimeVisualTheme.MoonWhite, 0.45f));
        Vector2[] mantle =
        [
            center + new Vector2(-rect.Size.X * 0.31f, rect.Size.Y * 0.28f),
            center + new Vector2(-rect.Size.X * 0.19f, -rect.Size.Y * 0.10f),
            center + new Vector2(0.0f, -rect.Size.Y * 0.18f),
            center + new Vector2(rect.Size.X * 0.19f, -rect.Size.Y * 0.10f),
            center + new Vector2(rect.Size.X * 0.31f, rect.Size.Y * 0.28f),
        ];
        DrawColoredPolygon(mantle, new Color(AnimeVisualTheme.Ink, 0.67f));
        DrawArc(center, rect.Size.X * 0.30f, -2.65f, -0.49f, 32, new Color(AnimeVisualTheme.PaleGold, 0.42f), MathF.Max(1.0f, 2.0f * scale), true);
    }

    private void DrawHiddenSigil(Rect2 rect, float scale)
    {
        Vector2 center = rect.GetCenter();
        float radius = MathF.Min(rect.Size.X, rect.Size.Y) * 0.28f;
        DrawArc(center, radius, 0.0f, MathF.Tau, 64, new Color(AnimeVisualTheme.OldGold, 0.82f), MathF.Max(1.0f, 2.0f * scale), true);
        DrawArc(center, radius * 0.58f, 0.0f, MathF.Tau, 64, new Color(AnimeVisualTheme.MoonWhite, 0.45f), MathF.Max(1.0f, scale), true);
        DrawLine(center - new Vector2(radius * 1.10f, radius * 0.74f), center + new Vector2(radius * 1.10f, radius * 0.74f), new Color(AnimeVisualTheme.PactViolet, 0.96f), MathF.Max(2.0f, 4.0f * scale), true);
        DrawLine(center - new Vector2(radius * 0.78f, radius * 1.04f), center + new Vector2(radius * 0.78f, radius * 1.04f), new Color(AnimeVisualTheme.OathBlue, 0.82f), MathF.Max(1.0f, 2.0f * scale), true);
    }

    private void DrawOrnaments(Rect2 bounds, Rect2 artRect, Color faction, float scale)
    {
        float stroke = MathF.Max(1.0f, 1.35f * scale);
        Color gold = new(AnimeVisualTheme.OldGold, 0.70f);
        DrawLine(artRect.Position, artRect.Position + new Vector2(artRect.Size.X * 0.30f, 0.0f), gold, stroke, true);
        DrawLine(artRect.End, artRect.End - new Vector2(artRect.Size.X * 0.30f, 0.0f), gold, stroke, true);
        DrawArc(bounds.Size * 0.5f, MathF.Min(bounds.Size.X, bounds.Size.Y) * 0.45f, -2.55f, -0.58f, 28, new Color(faction, 0.32f), stroke, true);
    }

    private void DrawTypeMarker(float scale, Color faction)
    {
        Vector2 center = TypeMarkerCenter;
        float radius = TypeMarkerRadius;
        float stroke = MathF.Max(1.5f, 1.8f * scale);
        Color shadow = new(AnimeVisualTheme.Ink, 0.92f);
        Color fill = new(faction.Darkened(0.28f), 0.98f);
        Color outline = new(AnimeVisualTheme.PaleGold, 0.98f);
        DrawCircle(center + new Vector2(0.0f, MathF.Max(1.0f, 1.5f * scale)), radius + 1.5f, shadow);

        switch (_kind)
        {
            case AnimeCardKind.Follower:
            {
                Vector2[] shield =
                [
                    center + new Vector2(-radius * 0.76f, -radius * 0.70f),
                    center + new Vector2(0.0f, -radius * 0.96f),
                    center + new Vector2(radius * 0.76f, -radius * 0.70f),
                    center + new Vector2(radius * 0.66f, radius * 0.30f),
                    center + new Vector2(0.0f, radius * 0.96f),
                    center + new Vector2(-radius * 0.66f, radius * 0.30f),
                ];
                DrawMarkerPolygon(shield, fill, outline, stroke);
                break;
            }
            case AnimeCardKind.Spell:
            {
                Vector2[] star =
                [
                    center + new Vector2(0.0f, -radius),
                    center + new Vector2(radius * 0.28f, -radius * 0.30f),
                    center + new Vector2(radius, 0.0f),
                    center + new Vector2(radius * 0.28f, radius * 0.30f),
                    center + new Vector2(0.0f, radius),
                    center + new Vector2(-radius * 0.28f, radius * 0.30f),
                    center + new Vector2(-radius, 0.0f),
                    center + new Vector2(-radius * 0.28f, -radius * 0.30f),
                ];
                DrawMarkerPolygon(star, fill, outline, stroke);
                break;
            }
            case AnimeCardKind.Amulet:
                DrawCircle(center, radius * 0.94f, fill);
                DrawArc(center, radius * 0.94f, 0.0f, MathF.Tau, 40, outline, stroke, true);
                DrawArc(center, radius * 0.61f, 0.0f, MathF.Tau, 32, new Color(AnimeVisualTheme.MoonWhite, 0.80f), stroke, true);
                break;
            case AnimeCardKind.Trap:
            {
                Vector2[] triangle =
                [
                    center + new Vector2(-radius * 0.96f, -radius * 0.72f),
                    center + new Vector2(radius * 0.96f, -radius * 0.72f),
                    center + new Vector2(0.0f, radius),
                ];
                DrawMarkerPolygon(triangle, fill, outline, stroke);
                break;
            }
            case AnimeCardKind.Field:
            {
                Rect2 gate = new(
                    center - new Vector2(radius * 0.88f, radius * 0.82f),
                    new Vector2(radius * 1.76f, radius * 1.64f));
                DrawRect(gate, fill);
                DrawPolyline(
                    [gate.Position, new Vector2(gate.End.X, gate.Position.Y), gate.End, new Vector2(gate.Position.X, gate.End.Y), gate.Position],
                    outline,
                    stroke,
                    true);
                DrawLine(
                    new Vector2(gate.Position.X, center.Y - (radius * 0.10f)),
                    new Vector2(gate.End.X, center.Y - (radius * 0.10f)),
                    new Color(AnimeVisualTheme.MoonWhite, 0.76f),
                    stroke,
                    true);
                break;
            }
        }

        int glyphSize = Math.Max(11, (int)MathF.Round(15.0f * scale));
        DrawString(
            AnimeVisualTheme.DisplayFont,
            center + new Vector2(-radius, glyphSize * 0.35f),
            TypeMarkerGlyph,
            HorizontalAlignment.Center,
            radius * 2.0f,
            glyphSize,
            Colors.White);
    }

    private void DrawMarkerPolygon(Vector2[] points, Color fill, Color outline, float stroke)
    {
        DrawColoredPolygon(points, fill);
        Vector2[] closed = [.. points, points[0]];
        DrawPolyline(closed, outline, stroke, true);
    }

    private void DrawBadge(Vector2 center, float radius, string text, Color fill, float scale)
    {
        DrawCircle(center + new Vector2(0.0f, 1.5f * scale), radius + (2.0f * scale), new Color(AnimeVisualTheme.Ink, 0.88f));
        DrawCircle(center, radius, fill.Darkened(0.36f));
        DrawArc(center, radius - MathF.Max(1.0f, scale), 0.0f, MathF.Tau, 40, new Color(AnimeVisualTheme.PaleGold, 0.94f), MathF.Max(1.0f, 1.6f * scale), true);
        int fontSize = BadgeFontPixelSize;
        DrawString(
            AnimeVisualTheme.DisplayFont,
            center + new Vector2(-radius, fontSize * 0.35f),
            text,
            HorizontalAlignment.Center,
            radius * 2.0f,
            fontSize,
            Colors.White);
    }

    private Rect2 BadgeLocalRect(Vector2 center, float radius)
    {
        float extent = radius + MathF.Max(2.0f, 2.0f * CardScale);
        return new Rect2(center - (Vector2.One * extent), Vector2.One * extent * 2.0f);
    }

    private Rect2 TransformLocalRect(Rect2 localRect)
    {
        Transform2D transform = GetGlobalTransformWithCanvas();
        Vector2 topLeft = transform * localRect.Position;
        Vector2 topRight = transform * new Vector2(localRect.End.X, localRect.Position.Y);
        Vector2 bottomRight = transform * localRect.End;
        Vector2 bottomLeft = transform * new Vector2(localRect.Position.X, localRect.End.Y);
        float minX = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomRight.X, bottomLeft.X));
        float minY = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomRight.Y, bottomLeft.Y));
        float maxX = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomRight.X, bottomLeft.X));
        float maxY = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomRight.Y, bottomLeft.Y));
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private static string Shorten(string value, int maximumCharacters)
    {
        if (value.Length <= maximumCharacters)
        {
            return value;
        }
        return value[..Math.Max(1, maximumCharacters - 1)] + "…";
    }
}

internal sealed partial class AnimeRuneSlot : Control
{
    private AnimeFaction _faction;
    private AnimeCardKind _kind;
    private bool _active;

    internal void Configure(AnimeFaction faction, AnimeCardKind kind, bool active = false)
    {
        _faction = faction;
        _kind = kind;
        _active = active;
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 center = Size * 0.5f;
        Color accent = AnimeVisualTheme.FactionColor(_faction);
        float radius = MathF.Min(Size.X, Size.Y) * 0.42f;
        Color fill = new(accent.Darkened(0.45f), _active ? 0.24f : 0.10f);
        DrawCircle(center, radius, fill);
        DrawArc(center, radius, 0.0f, MathF.Tau, 56, new Color(accent, _active ? 0.88f : 0.30f), _active ? 2.3f : 1.2f, true);
        DrawArc(center, radius * 0.72f, 0.0f, MathF.Tau, 48, new Color(AnimeVisualTheme.OldGold, _active ? 0.68f : 0.20f), 1.0f, true);
        Vector2[] diamond =
        [
            center + new Vector2(0.0f, -radius * 0.45f),
            center + new Vector2(radius * 0.34f, 0.0f),
            center + new Vector2(0.0f, radius * 0.45f),
            center + new Vector2(-radius * 0.34f, 0.0f),
        ];
        DrawPolyline(diamond.Append(diamond[0]).ToArray(), new Color(AnimeVisualTheme.MoonWhite, _active ? 0.84f : 0.28f), 1.4f, true);
        if (_active)
        {
            string glyph = _kind switch
            {
                AnimeCardKind.Field => "场",
                AnimeCardKind.Trap => "策",
                _ => "主",
            };
            DrawString(AnimeVisualTheme.DisplayFont, center + new Vector2(-14.0f, 6.0f), glyph, HorizontalAlignment.Center, 28.0f, 15, AnimeVisualTheme.MoonWhite);
        }
    }
}
