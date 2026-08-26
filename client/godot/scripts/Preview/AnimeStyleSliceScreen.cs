// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Ci;

namespace Scgs.GodotClient.Preview;

/// <summary>
/// A no-native, no-rules visual direction slice. It exists to approve the
/// AnimeV1 composition and asset language before the product session migrates
/// to schema 2. No DTO in this scene may be mistaken for playable state.
/// </summary>
public sealed partial class AnimeStyleSliceScreen : Control
{
    internal const string StateMenu = "menu";
    internal const string StateSetup = "setup";
    internal const string StateAction = "action";
    internal const string StateHandHover = "hand-hover";
    internal const string StateMixed = "mixed-permanents-field";
    internal const string StateReaction = "reaction";
    internal const string StateCovered = "covered";
    internal const string StateResult = "result";

    internal static IReadOnlyList<string> States { get; } =
    [
        StateMenu,
        StateSetup,
        StateAction,
        StateHandHover,
        StateMixed,
        StateReaction,
        StateCovered,
        StateResult,
    ];

    private readonly List<AnimeCardPreview> _cards = [];
    private readonly List<AnimeRuneSlot> _slots = [];
    private readonly List<AnimeLeaderCore> _leaderCores = [];
    private AnimeVisualSliceLaunch _launch = new(false, null, false, StateMenu);
    private AnimeSliceMotionProfile _motionProfile = AnimeSliceMotionProfile.Disabled;
    private AnimeBackdropCanvas _backdrop = null!;
    private Control _content = null!;
    private PanelContainer _toolbar = null!;
    private string _state = StateMenu;
    private Rect2 _boardRect;
    private Rect2 _leftPanelRect;
    private Rect2 _rightPanelRect;
    private bool _coveredOpaque;
    private bool _captureStarted;
    private bool _ready;

    internal string CurrentState => _state;

    internal void Configure(AnimeVisualSliceLaunch launch)
    {
        ArgumentNullException.ThrowIfNull(launch);
        if (_ready)
        {
            throw new InvalidOperationException("The anime visual slice must be configured before entering the tree.");
        }
        _launch = launch;
    }

    public override void _Ready()
    {
        _ready = true;
        _motionProfile = AnimeSliceMotionPolicy.Select(_launch.OutputDirectory);
        MouseFilter = MouseFilterEnum.Pass;
        _backdrop = new AnimeBackdropCanvas
        {
            Name = "AnimeBackdrop",
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _backdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_backdrop);

        _content = new Control
        {
            Name = "SliceContent",
            MouseFilter = MouseFilterEnum.Pass,
        };
        _content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_content);

        _toolbar = BuildToolbar();
        AddChild(_toolbar);
        Resized += OnResized;
        SetPreviewState(_launch.InitialState);

        if (_launch.OutputDirectory is not null)
        {
            _toolbar.Visible = false;
            Callable.From(RunCaptureSuite).CallDeferred();
        }
    }

    public override void _ExitTree()
    {
        Resized -= OnResized;
    }

    internal void SetPreviewState(string state)
    {
        if (!States.Contains(state, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown AnimeV1 slice state.");
        }
        _state = state;
        Rebuild();
    }

    internal AnimeSliceLayoutEvidence MeasureLayout()
    {
        Rect2 viewport = new(Vector2.Zero, Size);
        int hiddenCards = _cards.Count(card => card.IsHidden);
        int hiddenCardsWithIdentity = _cards.Count(card => card.IsHidden && card.ShowsIdentity);
        string[] kinds = _cards
            .Where(card => !card.IsHidden)
            .Select(card => card.Kind.ToString())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        return new AnimeSliceLayoutEvidence
        {
            State = _state,
            Viewport = AnimeSliceRect.From(viewport),
            Board = AnimeSliceRect.From(_boardRect),
            LeftPanel = AnimeSliceRect.From(_leftPanelRect),
            RightPanel = AnimeSliceRect.From(_rightPanelRect),
            HasOuterTableFrame = false,
            UsesNativeSession = false,
            MainBoardSlotCount = _slots.Count(slot => slot.Name.ToString().StartsWith("Main", StringComparison.Ordinal)),
            TacticSlotCount = _slots.Count(slot => slot.Name.ToString().StartsWith("Tactic", StringComparison.Ordinal)),
            FieldSlotCount = _slots.Count(slot => slot.Name.ToString().StartsWith("Field", StringComparison.Ordinal)),
            VisibleCardCount = _cards.Count - hiddenCards,
            HiddenCardCount = hiddenCards,
            HiddenCardsWithIdentity = hiddenCardsWithIdentity,
            VisibleCardKinds = kinds,
            CoveredOpaque = _coveredOpaque,
            LoadedAssetCount = AnimeVisualAssetCatalog.LoadedPaths().Count,
            RequiredAssetCount = AnimeVisualAssetCatalog.RequiredPaths.Count,
        };
    }

    private void OnResized()
    {
        if (_ready && Size.X >= 640.0f && Size.Y >= 360.0f)
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        if (!_ready || _content is null || Size.X < 1.0f || Size.Y < 1.0f)
        {
            return;
        }
        foreach (Node child in _content.GetChildren())
        {
            child.Free();
        }
        _cards.Clear();
        _slots.Clear();
        _leaderCores.Clear();
        _boardRect = new Rect2();
        _leftPanelRect = new Rect2();
        _rightPanelRect = new Rect2();
        _coveredOpaque = false;

        _backdrop.Configure(_state);
        switch (_state)
        {
            case StateMenu:
                BuildMenu();
                break;
            case StateSetup:
                BuildSetup();
                break;
            case StateAction:
            case StateHandHover:
            case StateMixed:
            case StateReaction:
                BuildBattle(_state);
                break;
            case StateCovered:
                BuildCovered();
                break;
            case StateResult:
                BuildResult();
                break;
        }
        _toolbar.MoveToFront();
    }

    private PanelContainer BuildToolbar()
    {
        var panel = new PanelContainer
        {
            Name = "PreviewToolbar",
            Position = new Vector2(18.0f, 14.0f),
            Size = new Vector2(714.0f, 42.0f),
            MouseFilter = MouseFilterEnum.Stop,
        };
        panel.AddThemeStyleboxOverride("panel", AnimeVisualTheme.Panel(AnimeVisualTheme.DeepIndigo, 0.82f, 12, 1));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 5);
        panel.AddChild(row);
        string[] captions = ["菜单", "设置", "对局", "手牌", "永久物", "响应", "交接", "结果"];
        for (int index = 0; index < States.Count; index++)
        {
            string state = States[index];
            var button = new Button
            {
                Text = captions[index],
                CustomMinimumSize = new Vector2(70.0f, 30.0f),
            };
            AnimeVisualTheme.ApplyButton(button, index < 4 ? AnimeFaction.Oathguard : AnimeFaction.Pactmage);
            button.AddThemeFontSizeOverride("font_size", 13);
            button.Pressed += () => SetPreviewState(state);
            row.AddChild(button);
        }
        return panel;
    }

    private void BuildMenu()
    {
        float scale = Math.Clamp(Size.Y / 900.0f, 0.80f, 1.45f);
        AddLabel(
            _content,
            new Rect2(54.0f * scale, 72.0f * scale, 430.0f * scale, 34.0f * scale),
            "SomeCardGameShit · 开发代号",
            14,
            new Color(AnimeVisualTheme.MoonWhite, 0.68f));
        AddLabel(
            _content,
            new Rect2(58.0f * scale, 160.0f * scale, 660.0f * scale, 92.0f * scale),
            "曜誓  ×  渊契",
            (int)(50 * scale),
            AnimeVisualTheme.MoonWhite);
        AddLabel(
            _content,
            new Rect2(62.0f * scale, 246.0f * scale, 500.0f * scale, 42.0f * scale),
            "原创日式幻想动漫视觉方向样片",
            (int)(19 * scale),
            new Color(AnimeVisualTheme.PaleGold, 0.92f));

        float navWidth = Math.Clamp(Size.X * 0.25f, 288.0f, 410.0f);
        Rect2 navRect = new(
            Size.X - navWidth - (54.0f * scale),
            120.0f * scale,
            navWidth,
            MathF.Min(Size.Y - (180.0f * scale), 612.0f * scale));
        Panel nav = AddPanel(_content, navRect, AnimeVisualTheme.DeepIndigo, 0.66f, 24);
        var column = new VBoxContainer
        {
            Position = new Vector2(20.0f, 18.0f),
            Size = navRect.Size - new Vector2(40.0f, 36.0f),
        };
        column.AddThemeConstantOverride("separation", (int)(12 * scale));
        nav.AddChild(column);
        AddContainerLabel(column, "战斗大厅", (int)(25 * scale), AnimeVisualTheme.PaleGold);
        AddContainerLabel(column, "选择你的誓约，让日轮或裂隙回应。", (int)(14 * scale), new Color(AnimeVisualTheme.MoonWhite, 0.72f));
        string[] entries = ["本地热座", "单人挑战", "牌组编辑", "卡牌图鉴", "设置"];
        for (int index = 0; index < entries.Length; index++)
        {
            var button = new Button
            {
                Text = entries[index],
                CustomMinimumSize = new Vector2(0.0f, 54.0f * scale),
                Disabled = index is 1 or 2 or 3,
            };
            AnimeVisualTheme.ApplyButton(button, index == 0 ? AnimeFaction.Oathguard : AnimeFaction.Neutral, index == 0);
            column.AddChild(button);
        }
        AddContainerLabel(column, "战斗功能尚未接入新产品牌组", (int)(12 * scale), new Color(AnimeVisualTheme.MoonWhite, 0.52f));

        AddFactionSigil(
            _content,
            new Rect2(68.0f * scale, Size.Y - (220.0f * scale), 190.0f * scale, 190.0f * scale),
            AnimeFaction.Oathguard);
        AddFactionSigil(
            _content,
            new Rect2(258.0f * scale, Size.Y - (220.0f * scale), 190.0f * scale, 190.0f * scale),
            AnimeFaction.Pactmage);
    }

    private void BuildSetup()
    {
        float margin = Math.Clamp(Size.X * 0.045f, 42.0f, 94.0f);
        AddLabel(_content, new Rect2(margin, 46.0f, Size.X - (margin * 2.0f), 52.0f), "选择两席的职业与系列", 30, AnimeVisualTheme.MoonWhite, HorizontalAlignment.Center);
        AddLabel(_content, new Rect2(margin, 94.0f, Size.X - (margin * 2.0f), 34.0f), "产品牌组尚未接入规则；此页仅用于确认角色、卡框与信息密度", 15, new Color(AnimeVisualTheme.MoonWhite, 0.60f), HorizontalAlignment.Center);

        float gap = Math.Clamp(Size.X * 0.028f, 28.0f, 58.0f);
        float panelWidth = MathF.Min(610.0f, (Size.X - (margin * 2.0f) - gap) * 0.5f);
        float panelHeight = Size.Y - 202.0f;
        Rect2 oathRect = new(margin, 136.0f, panelWidth, panelHeight);
        Rect2 pactRect = new(Size.X - margin - panelWidth, 136.0f, panelWidth, panelHeight);
        BuildDeckPanel(oathRect, AnimeFaction.Oathguard, "誓卫", "曜誓骑士团", "曜誓圣王女·奥蕾莉亚", AnimeVisualAssetCatalog.AureliaLeader, "清偿裂痕 · 守护反攻 · 屏障联动");
        BuildDeckPanel(pactRect, AnimeFaction.Pactmage, "契术", "渊契魔导院", "渊契院长·瑟蕾娅", AnimeVisualAssetCatalog.SereiaLeader, "主动预支 · 裂痕阈值 · 疾驰清算");

        var start = new Button
        {
            Text = "进入视觉演示",
            Position = new Vector2((Size.X - 230.0f) * 0.5f, Size.Y - 70.0f),
            Size = new Vector2(230.0f, 50.0f),
        };
        AnimeVisualTheme.ApplyButton(start, AnimeFaction.Neutral, primary: true);
        start.Pressed += () => SetPreviewState(StateAction);
        _content.AddChild(start);
    }

    private void BuildDeckPanel(
        Rect2 rect,
        AnimeFaction faction,
        string profession,
        string series,
        string leaderName,
        string portraitPath,
        string summary)
    {
        Panel panel = AddPanel(_content, rect, faction == AnimeFaction.Oathguard ? new Color("263b66") : new Color("3b234f"), 0.74f, 24);
        var portrait = new AnimePortraitPreview
        {
            Position = new Vector2(18.0f, 20.0f),
            Size = new Vector2(rect.Size.X * 0.46f, rect.Size.Y - 40.0f),
        };
        portrait.Configure(faction, portraitPath, leaderName, motionProfile: _motionProfile);
        panel.AddChild(portrait);
        AddLabel(panel, new Rect2(rect.Size.X * 0.50f, 38.0f, rect.Size.X * 0.44f, 38.0f), profession, 28, AnimeVisualTheme.PaleGold);
        AddLabel(panel, new Rect2(rect.Size.X * 0.50f, 82.0f, rect.Size.X * 0.44f, 35.0f), series, 20, AnimeVisualTheme.MoonWhite);
        AddLabel(panel, new Rect2(rect.Size.X * 0.50f, 126.0f, rect.Size.X * 0.44f, 68.0f), leaderName, 16, new Color(AnimeVisualTheme.MoonWhite, 0.78f));
        AddLabel(panel, new Rect2(rect.Size.X * 0.50f, 220.0f, rect.Size.X * 0.42f, 96.0f), summary, 16, new Color(AnimeVisualTheme.MoonWhite, 0.72f));
        AddLabel(panel, new Rect2(rect.Size.X * 0.50f, rect.Size.Y - 106.0f, rect.Size.X * 0.42f, 42.0f), "30张主牌 · 4张公开战备", 14, AnimeVisualTheme.PaleGold);
    }

    private void BuildBattle(string state)
    {
        float width = Size.X;
        float height = Size.Y;
        float leftWidth = width < 1450.0f ? 232.0f : width < 2200.0f ? 284.0f : 320.0f;
        float rightWidth = width < 1450.0f ? 194.0f : width < 2200.0f ? 232.0f : 264.0f;
        float gutter = width < 1450.0f ? 12.0f : 18.0f;
        _leftPanelRect = new Rect2(gutter, 72.0f, leftWidth, height - 104.0f);
        _rightPanelRect = new Rect2(width - rightWidth - gutter, 72.0f, rightWidth, height - 104.0f);
        _rightPanelRect = new Rect2(
            _rightPanelRect.Position,
            new Vector2(_rightPanelRect.Size.X, MathF.Min(_rightPanelRect.Size.Y, 548.0f)));
        _boardRect = new Rect2(
            _leftPanelRect.End.X + gutter,
            30.0f,
            _rightPanelRect.Position.X - _leftPanelRect.End.X - (gutter * 2.0f),
            height - 48.0f);

        BuildCardDetail(_leftPanelRect);
        BuildBattleHud(_rightPanelRect);
        float centerX = _boardRect.GetCenter().X;
        float spacing = MathF.Min(112.0f, _boardRect.Size.X / 7.2f);
        float mainW = Math.Clamp(spacing * 0.72f, 62.0f, 86.0f);
        float mainH = mainW * 0.82f;
        float farMainY = _boardRect.Position.Y + (_boardRect.Size.Y * 0.31f);
        float nearMainY = _boardRect.Position.Y + (_boardRect.Size.Y * 0.53f);
        float farTacticY = _boardRect.Position.Y + (_boardRect.Size.Y * 0.18f);
        float nearTacticY = _boardRect.Position.Y + (_boardRect.Size.Y * 0.665f);

        for (int side = 0; side < 2; side++)
        {
            AnimeFaction faction = side == 0 ? AnimeFaction.Pactmage : AnimeFaction.Oathguard;
            float mainY = side == 0 ? farMainY : nearMainY;
            for (int index = 0; index < 5; index++)
            {
                float x = centerX + ((index - 2) * spacing) - (mainW * 0.5f);
                AddRune(new Rect2(x, mainY, mainW, mainH), $"Main{side}-{index}", faction, AnimeCardKind.Follower, state == StateAction && side == 1 && index == 3);
            }
            float tacticSpacing = spacing * 1.08f;
            float tacticY = side == 0 ? farTacticY : nearTacticY;
            for (int index = 0; index < 3; index++)
            {
                float x = centerX + ((index - 1) * tacticSpacing) - (mainW * 0.5f);
                AddRune(new Rect2(x, tacticY, mainW, mainH * 0.78f), $"Tactic{side}-{index}", faction, AnimeCardKind.Trap);
            }
            float fieldX = side == 0 ? _boardRect.Position.X + 38.0f : _boardRect.End.X - mainW - 38.0f;
            AddRune(new Rect2(fieldX, mainY + 5.0f, mainW, mainH), $"Field{side}", faction, AnimeCardKind.Field, state == StateMixed);
        }

        BuildLeaderCore(new Vector2(centerX, farMainY - 35.0f), AnimeFaction.Pactmage, "瑟蕾娅", "25", far: true);
        BuildLeaderCore(new Vector2(centerX, nearMainY + mainH + 42.0f), AnimeFaction.Oathguard, "奥蕾莉亚", "21", far: false);
        BuildFarHand(centerX, _boardRect.Position.Y + 6.0f);
        BuildNearHand(centerX, height, state == StateHandHover);
        BuildBoardCards(state, centerX, spacing, mainW, mainH, farMainY, nearMainY);

        if (state == StateReaction)
        {
            BuildReactionOverlay();
        }
    }

    private void BuildCardDetail(Rect2 rect)
    {
        Panel panel = AddPanel(_content, rect, new Color("251f3e"), 0.63f, 22);
        AddLabel(panel, new Rect2(18.0f, 16.0f, rect.Size.X - 36.0f, 30.0f), "卡牌详情", 18, AnimeVisualTheme.PaleGold);
        float cardW = MathF.Min(rect.Size.X - 32.0f, 280.0f);
        float cardH = cardW * 1.50f;
        AnimeCardPreview card = AddCard(
            panel,
            new Rect2((rect.Size.X - cardW) * 0.5f, 56.0f, cardW, cardH),
            "LO-11",
            "曜誓大团长·蕾奥妮",
            AnimeCardKind.Follower,
            AnimeFaction.Oathguard,
            10,
            8,
            8);
        card.Name = "PinnedDetailCard";
        AddLabel(panel, new Rect2(18.0f, 68.0f + cardH, rect.Size.X - 36.0f, 28.0f), "守护", 17, AnimeVisualTheme.OathBlue);
        Label rules = AddLabel(panel, new Rect2(18.0f, 102.0f + cardH, rect.Size.X - 36.0f, rect.Size.Y - cardH - 126.0f), "登场：若按期打出且无隙，获得疾驰与屏障；否则获得突进。", 14, new Color(AnimeVisualTheme.MoonWhite, 0.78f));
        rules.VerticalAlignment = VerticalAlignment.Top;
    }

    private void BuildBattleHud(Rect2 rect)
    {
        Panel panel = AddPanel(_content, rect, new Color("211a36"), 0.54f, 20);
        BuildStatusCapsule(panel, new Rect2(14.0f, 16.0f, rect.Size.X - 28.0f, 104.0f), AnimeFaction.Pactmage, "玩家 2 · 契术", "25", "PP 5/6", "裂痕 4");
        BuildStatusCapsule(panel, new Rect2(14.0f, 132.0f, rect.Size.X - 28.0f, 104.0f), AnimeFaction.Oathguard, "玩家 1 · 誓卫", "21", "PP 7/7", "裂痕 0");
        AddLabel(panel, new Rect2(18.0f, 258.0f, rect.Size.X - 36.0f, 30.0f), "第 8 回合", 17, AnimeVisualTheme.PaleGold, HorizontalAlignment.Center);
        AddLabel(panel, new Rect2(18.0f, 284.0f, rect.Size.X - 36.0f, 24.0f), "行动阶段", 13, new Color(AnimeVisualTheme.MoonWhite, 0.62f), HorizontalAlignment.Center);
        var endTurn = new Button
        {
            Text = "结束回合",
            Position = new Vector2(18.0f, 316.0f),
            Size = new Vector2(rect.Size.X - 36.0f, 58.0f),
        };
        AnimeVisualTheme.ApplyButton(endTurn, AnimeFaction.Oathguard, primary: true);
        panel.AddChild(endTurn);
        AddLabel(panel, new Rect2(18.0f, 392.0f, rect.Size.X - 36.0f, 38.0f), "事件记录  ∨", 15, new Color(AnimeVisualTheme.MoonWhite, 0.64f));
        if (_motionProfile.Enabled && _state is StateAction or StateMixed)
        {
            var hitPreview = new Button
            {
                Text = "演示：主战者受击",
                Position = new Vector2(18.0f, 432.0f),
                Size = new Vector2(rect.Size.X - 36.0f, 36.0f),
                TooltipText = "仅触发视觉脉冲，不调用规则或原生会话",
            };
            AnimeVisualTheme.ApplyButton(hitPreview, AnimeFaction.Pactmage);
            hitPreview.Pressed += TriggerHitPreview;
            panel.AddChild(hitPreview);
        }
        AddLabel(panel, new Rect2(18.0f, rect.Size.Y - 72.0f, rect.Size.X - 36.0f, 46.0f), "Esc  暂停    F3  调试", 12, new Color(AnimeVisualTheme.MoonWhite, 0.38f), HorizontalAlignment.Center);
    }

    private void BuildStatusCapsule(Control parent, Rect2 rect, AnimeFaction faction, string name, string health, string pp, string cracks)
    {
        Panel panel = AddPanel(parent, rect, faction == AnimeFaction.Oathguard ? new Color("273759") : new Color("3e264e"), 0.80f, 16);
        var portrait = new AnimePortraitPreview
        {
            Position = new Vector2(9.0f, 9.0f),
            Size = new Vector2(78.0f, 78.0f),
        };
        portrait.Configure(
            faction,
            faction == AnimeFaction.Oathguard ? AnimeVisualAssetCatalog.AureliaLeader : AnimeVisualAssetCatalog.SereiaLeader,
            name,
            medallion: true);
        panel.AddChild(portrait);
        AddLabel(panel, new Rect2(92.0f, 10.0f, rect.Size.X - 100.0f, 24.0f), name, 14, AnimeVisualTheme.MoonWhite);
        AddLabel(panel, new Rect2(92.0f, 38.0f, 48.0f, 36.0f), health, 27, faction == AnimeFaction.Oathguard ? AnimeVisualTheme.OathBlue : AnimeVisualTheme.PactCrimson);
        AddLabel(panel, new Rect2(142.0f, 42.0f, rect.Size.X - 148.0f, 24.0f), pp, 13, AnimeVisualTheme.PaleGold);
        AddLabel(panel, new Rect2(142.0f, 67.0f, rect.Size.X - 148.0f, 22.0f), cracks, 12, new Color(AnimeVisualTheme.MoonWhite, 0.64f));
    }

    private void BuildLeaderCore(Vector2 center, AnimeFaction faction, string name, string health, bool far)
    {
        var core = new AnimeLeaderCore
        {
            Position = center - new Vector2(72.0f, far ? 39.0f : 34.0f),
            Size = new Vector2(144.0f, 68.0f),
            Name = far ? "FarLeaderCore" : "NearLeaderCore",
        };
        core.Configure(faction, name, health, far, _motionProfile);
        _leaderCores.Add(core);
        _content.AddChild(core);
    }

    private void TriggerHitPreview()
    {
        if (!_motionProfile.Enabled)
        {
            return;
        }
        AnimeLeaderCore? farLeader = _leaderCores.FirstOrDefault(
            core => core.Name.ToString() == "FarLeaderCore");
        farLeader?.TriggerHitPulse();
    }

    private void BuildFarHand(float centerX, float y)
    {
        const float width = 58.0f;
        const float height = 87.0f;
        const float spacing = 34.0f;
        for (int index = 0; index < 5; index++)
        {
            AnimeCardPreview card = AddCard(
                _content,
                new Rect2(centerX + ((index - 2) * spacing) - (width * 0.5f), y, width, height),
                string.Empty,
                string.Empty,
                AnimeCardKind.Follower,
                AnimeFaction.Neutral,
                0,
                hidden: true);
            card.Name = $"FarHand{index}";
            card.RotationDegrees = (index - 2) * 2.3f;
        }
    }

    private void BuildNearHand(float centerX, float viewportHeight, bool hovered)
    {
        float cardHeight = Math.Clamp(viewportHeight * 0.205f, 142.0f, 190.0f);
        float cardWidth = cardHeight / 1.50f;
        float spacing = Math.Clamp(cardWidth * 0.78f, 76.0f, 102.0f);
        (string Id, string Name, AnimeCardKind Kind, AnimeFaction Faction, int Cost, int? Attack, int? Health, int? Countdown)[] cards =
        [
            ("LO-03", "晨钟誓碑", AnimeCardKind.Amulet, AnimeFaction.Oathguard, 2, null, null, 3),
            ("LO-07", "曜誓·不破阵", AnimeCardKind.Trap, AnimeFaction.Oathguard, 2, null, null, null),
            ("LO-11", "曜誓大团长·蕾奥妮", AnimeCardKind.Follower, AnimeFaction.Oathguard, 10, 8, 8, null),
            ("NT-04", "界域裁定", AnimeCardKind.Spell, AnimeFaction.Neutral, 4, null, null, null),
            ("LO-03", "晨钟誓碑", AnimeCardKind.Amulet, AnimeFaction.Oathguard, 2, null, null, 3),
        ];
        AnimeCardPreview? focusedCard = null;
        for (int index = 0; index < cards.Length; index++)
        {
            bool focus = hovered && index == 2;
            float scale = focus ? 1.12f : 1.0f;
            float spread = hovered ? MathF.Abs(index - 2) == 1 ? 23.0f : MathF.Abs(index - 2) == 2 ? 46.0f : 0.0f : 0.0f;
            float direction = MathF.Sign(index - 2);
            float x = centerX + ((index - 2) * spacing) + (spread * direction) - ((cardWidth * scale) * 0.5f);
            float restingY = viewportHeight - cardHeight - 32.0f + (MathF.Abs(index - 2) * 5.0f);
            float y = focus
                ? viewportHeight - (cardHeight * scale) - 34.0f
                : restingY;
            AnimeCardPreview card = AddCard(
                _content,
                new Rect2(x, y, cardWidth * scale, cardHeight * scale),
                cards[index].Id,
                cards[index].Name,
                cards[index].Kind,
                cards[index].Faction,
                cards[index].Cost,
                cards[index].Attack,
                cards[index].Health,
                cards[index].Countdown);
            card.Name = $"NearHand{index}";
            card.PivotOffset = card.Size * 0.5f;
            card.RotationDegrees = focus ? 0.0f : (index - 2) * 3.6f;
            if (focus)
            {
                focusedCard = card;
            }
        }
        focusedCard?.MoveToFront();
    }

    private void BuildBoardCards(string state, float centerX, float spacing, float slotW, float slotH, float farY, float nearY)
    {
        float cardW = slotW * 0.88f;
        float cardH = cardW * 1.50f;
        AddCard(_content, new Rect2(centerX - spacing - (cardW * 0.5f), nearY - (cardH * 0.42f), cardW, cardH), "LO-03", "晨钟誓碑", AnimeCardKind.Amulet, AnimeFaction.Oathguard, 2, countdown: 2).Name = "NearAmulet";
        AddCard(_content, new Rect2(centerX - (cardW * 0.5f), nearY - (cardH * 0.42f), cardW, cardH), "LO-11", "曜誓大团长·蕾奥妮", AnimeCardKind.Follower, AnimeFaction.Oathguard, 10, 8, 8).Name = "NearFollower";
        AddCard(_content, new Rect2(centerX + spacing - (cardW * 0.5f), farY - (cardH * 0.42f), cardW, cardH), "AP-11", "禁忌毕业生·诺克缇娅", AnimeCardKind.Follower, AnimeFaction.Pactmage, 8, 6, 6).Name = "FarFollower";

        if (state == StateMixed || state == StateReaction)
        {
            float fieldX = _boardRect.End.X - slotW - 38.0f;
            AddCard(_content, new Rect2(fieldX + 4.0f, nearY - (cardH * 0.42f), cardW, cardH), "AP-05", "渊契魔导院·零时讲堂", AnimeCardKind.Field, AnimeFaction.Pactmage, 3).Name = "FieldPermanent";
            float tacticY = _boardRect.Position.Y + (_boardRect.Size.Y * 0.665f);
            AddCard(_content, new Rect2(centerX - (cardW * 0.5f), tacticY - (cardH * 0.54f), cardW, cardH), "LO-07", "曜誓·不破阵", AnimeCardKind.Trap, AnimeFaction.Oathguard, 2).Name = "TacticPermanent";
        }
    }

    private void BuildReactionOverlay()
    {
        float width = Math.Clamp(Size.X * 0.34f, 410.0f, 560.0f);
        Rect2 rect = new((Size.X - width) * 0.5f, Size.Y * 0.25f, width, 252.0f);
        Panel panel = AddPanel(_content, rect, new Color("302045"), 0.92f, 24);
        AddLabel(panel, new Rect2(24.0f, 20.0f, width - 48.0f, 34.0f), "响应机会", 24, AnimeVisualTheme.PaleGold, HorizontalAlignment.Center);
        AddLabel(panel, new Rect2(24.0f, 60.0f, width - 48.0f, 44.0f), "对方宣告攻击：是否发动伏策？", 16, AnimeVisualTheme.MoonWhite, HorizontalAlignment.Center);
        AddCard(panel, new Rect2(38.0f, 100.0f, 82.0f, 123.0f), "LO-07", "曜誓·不破阵", AnimeCardKind.Trap, AnimeFaction.Oathguard, 2).Name = "ReactionCandidate";
        var activate = new Button { Text = "发动", Position = new Vector2(150.0f, 122.0f), Size = new Vector2(width - 180.0f, 46.0f) };
        AnimeVisualTheme.ApplyButton(activate, AnimeFaction.Oathguard, primary: true);
        panel.AddChild(activate);
        var pass = new Button { Text = "不过", Position = new Vector2(150.0f, 178.0f), Size = new Vector2(width - 180.0f, 40.0f) };
        AnimeVisualTheme.ApplyButton(pass, AnimeFaction.Neutral);
        panel.AddChild(pass);
    }

    private void BuildCovered()
    {
        _coveredOpaque = true;
        AddFactionSigil(_content, new Rect2((Size.X * 0.5f) - 120.0f, (Size.Y * 0.5f) - 210.0f, 240.0f, 240.0f), AnimeFaction.Oathguard);
        AddLabel(_content, new Rect2(0.0f, (Size.Y * 0.5f) + 36.0f, Size.X, 50.0f), "请交给玩家 1", 32, AnimeVisualTheme.MoonWhite, HorizontalAlignment.Center);
        AddLabel(_content, new Rect2(0.0f, (Size.Y * 0.5f) + 88.0f, Size.X, 34.0f), "对方手牌与选择已完全清除", 15, new Color(AnimeVisualTheme.MoonWhite, 0.58f), HorizontalAlignment.Center);
        var reveal = new Button
        {
            Text = "揭示我的画面",
            Position = new Vector2((Size.X - 260.0f) * 0.5f, (Size.Y * 0.5f) + 144.0f),
            Size = new Vector2(260.0f, 58.0f),
        };
        AnimeVisualTheme.ApplyButton(reveal, AnimeFaction.Oathguard, primary: true);
        _content.AddChild(reveal);
    }

    private void BuildResult()
    {
        float portraitWidth = Math.Clamp(Size.X * 0.42f, 420.0f, 760.0f);
        var winningPortrait = new AnimePortraitPreview
        {
            Position = new Vector2(24.0f, 54.0f),
            Size = new Vector2(portraitWidth, Size.Y - 80.0f),
        };
        winningPortrait.Configure(
            AnimeFaction.Oathguard,
            AnimeVisualAssetCatalog.AureliaLeader,
            "曜誓圣王女·奥蕾莉亚",
            motionProfile: _motionProfile);
        winningPortrait.SetOutcome(AnimePortraitOutcome.Winner);
        _content.AddChild(winningPortrait);
        Rect2 panelRect = new(Size.X * 0.53f, Size.Y * 0.20f, Size.X * 0.38f, Size.Y * 0.58f);
        Panel panel = AddPanel(_content, panelRect, new Color("283b62"), 0.72f, 28);
        var losingPortrait = new AnimePortraitPreview
        {
            Position = new Vector2(panelRect.Size.X - 116.0f, 18.0f),
            Size = new Vector2(88.0f, 88.0f),
        };
        losingPortrait.Configure(
            AnimeFaction.Pactmage,
            AnimeVisualAssetCatalog.SereiaLeader,
            "渊契院长·瑟蕾娅",
            medallion: true,
            motionProfile: _motionProfile);
        losingPortrait.SetOutcome(AnimePortraitOutcome.Loser);
        panel.AddChild(losingPortrait);
        AddLabel(
            panel,
            new Rect2(panelRect.Size.X - 126.0f, 106.0f, 108.0f, 24.0f),
            "败方 · 契术",
            12,
            new Color(AnimeVisualTheme.MoonWhite, 0.42f),
            HorizontalAlignment.Center);
        AddLabel(panel, new Rect2(30.0f, 32.0f, panelRect.Size.X - 60.0f, 64.0f), "胜  利", 44, AnimeVisualTheme.PaleGold, HorizontalAlignment.Center);
        AddLabel(panel, new Rect2(30.0f, 108.0f, panelRect.Size.X - 60.0f, 42.0f), "誓卫 · 曜誓骑士团", 21, AnimeVisualTheme.MoonWhite, HorizontalAlignment.Center);
        AddLabel(panel, new Rect2(30.0f, 164.0f, panelRect.Size.X - 60.0f, 80.0f), "承诺已经兑现。\n日轮将照亮下一场战斗。", 17, new Color(AnimeVisualTheme.MoonWhite, 0.72f), HorizontalAlignment.Center);
        var again = new Button { Text = "再次对战", Position = new Vector2(36.0f, panelRect.Size.Y - 132.0f), Size = new Vector2(panelRect.Size.X - 72.0f, 50.0f) };
        AnimeVisualTheme.ApplyButton(again, AnimeFaction.Oathguard, primary: true);
        panel.AddChild(again);
        var menu = new Button { Text = "返回大厅", Position = new Vector2(36.0f, panelRect.Size.Y - 72.0f), Size = new Vector2(panelRect.Size.X - 72.0f, 42.0f) };
        AnimeVisualTheme.ApplyButton(menu, AnimeFaction.Neutral);
        menu.Pressed += () => SetPreviewState(StateMenu);
        panel.AddChild(menu);
    }

    private void AddRune(Rect2 rect, string name, AnimeFaction faction, AnimeCardKind kind, bool active = false)
    {
        var slot = new AnimeRuneSlot { Name = name, Position = rect.Position, Size = rect.Size };
        slot.Configure(faction, kind, active);
        _slots.Add(slot);
        _content.AddChild(slot);
    }

    private AnimeCardPreview AddCard(
        Control parent,
        Rect2 rect,
        string id,
        string name,
        AnimeCardKind kind,
        AnimeFaction faction,
        int cost,
        int? attack = null,
        int? health = null,
        int? countdown = null,
        bool hidden = false,
        bool evolved = false)
    {
        var card = new AnimeCardPreview { Position = rect.Position, Size = rect.Size };
        card.Configure(id, name, kind, faction, cost, attack, health, countdown, hidden, evolved);
        _cards.Add(card);
        parent.AddChild(card);
        return card;
    }

    private static Panel AddPanel(Control parent, Rect2 rect, Color tint, float alpha, int radius)
    {
        var panel = new Panel { Position = rect.Position, Size = rect.Size };
        panel.AddThemeStyleboxOverride("panel", AnimeVisualTheme.Panel(tint, alpha, radius, 1));
        parent.AddChild(panel);
        return panel;
    }

    private static Label AddLabel(
        Control parent,
        Rect2 rect,
        string text,
        int fontSize,
        Color color,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Position = rect.Position,
            Size = rect.Size,
            Text = text,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontOverride("font", AnimeVisualTheme.DisplayFont);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeColorOverride("font_shadow_color", new Color(AnimeVisualTheme.Ink, 0.80f));
        label.AddThemeConstantOverride("shadow_offset_x", 1);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        parent.AddChild(label);
        return label;
    }

    private static Label AddContainerLabel(Control parent, string text, int fontSize, Color color)
    {
        var label = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0.0f, fontSize * 2.2f),
        };
        label.AddThemeFontOverride("font", AnimeVisualTheme.DisplayFont);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }

    private static void AddFactionSigil(Control parent, Rect2 rect, AnimeFaction faction)
    {
        var sigil = new AnimeFactionSigil { Position = rect.Position, Size = rect.Size };
        sigil.Configure(faction);
        parent.AddChild(sigil);
    }

    private async void RunCaptureSuite()
    {
        if (_captureStarted)
        {
            return;
        }
        _captureStarted = true;
        try
        {
            var suite = new AnimeVisualSliceSuite(this, _launch.OutputDirectory!);
            string reportPath = await suite.RunAsync();
            GD.Print($"SCGS_ANIME_VISUAL_SLICE_READY report={reportPath} approval_status=pending_user_approval");
            if (_launch.ExitWhenComplete)
            {
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            GD.PrintErr($"SCGS_ANIME_VISUAL_SLICE_FAILED {exception}");
            if (_launch.ExitWhenComplete)
            {
                GetTree().Quit(1);
            }
        }
    }
}

internal sealed partial class AnimeBackdropCanvas : Control
{
    private string _state = AnimeStyleSliceScreen.StateMenu;
    private Texture2D? _texture;

    internal void Configure(string state)
    {
        _state = state;
        _texture = state == AnimeStyleSliceScreen.StateMenu
            ? AnimeVisualAssetCatalog.TryLoad(AnimeVisualAssetCatalog.MenuKeyArt)
            : state is AnimeStyleSliceScreen.StateAction or
                AnimeStyleSliceScreen.StateHandHover or
                AnimeStyleSliceScreen.StateMixed or
                AnimeStyleSliceScreen.StateReaction
                ? AnimeVisualAssetCatalog.TryLoad(AnimeVisualAssetCatalog.OpenArena)
                : null;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Rect2 rect = new(Vector2.Zero, Size);
        bool covered = _state == AnimeStyleSliceScreen.StateCovered;
        Color top = covered ? new Color("120f28") : new Color("211a42");
        Color bottom = covered ? new Color("080712") : new Color("080817");
        const int bands = 32;
        for (int index = 0; index < bands; index++)
        {
            float t = index / (float)(bands - 1);
            DrawRect(new Rect2(0.0f, rect.Size.Y * t, rect.Size.X, (rect.Size.Y / bands) + 2.0f), top.Lerp(bottom, t));
        }
        if (_texture is not null)
        {
            DrawTextureRect(_texture, rect, tile: false, new Color(1.0f, 1.0f, 1.0f, covered ? 0.0f : 0.72f));
        }

        if (_state is AnimeStyleSliceScreen.StateAction or
            AnimeStyleSliceScreen.StateHandHover or
            AnimeStyleSliceScreen.StateMixed or
            AnimeStyleSliceScreen.StateReaction)
        {
            DrawOpenArena();
        }
        else
        {
            DrawAtmosphere(covered);
        }
    }

    private void DrawOpenArena()
    {
        Vector2 vanishing = new(Size.X * 0.52f, Size.Y * 0.16f);
        Vector2 nearLeft = new(Size.X * 0.14f, Size.Y * 1.02f);
        Vector2 nearRight = new(Size.X * 0.88f, Size.Y * 1.02f);
        Vector2 farLeft = new(Size.X * 0.34f, Size.Y * 0.19f);
        Vector2 farRight = new(Size.X * 0.70f, Size.Y * 0.19f);
        for (int index = 0; index <= 12; index++)
        {
            float t = index / 12.0f;
            Vector2 near = nearLeft.Lerp(nearRight, t);
            DrawLine(vanishing, near, new Color(AnimeVisualTheme.OldGold, 0.10f), 1.0f, true);
        }
        for (int index = 0; index < 9; index++)
        {
            float t = index / 8.0f;
            float eased = t * t;
            float y = Mathf.Lerp(Size.Y * 0.21f, Size.Y * 0.98f, eased);
            float half = Mathf.Lerp(Size.X * 0.18f, Size.X * 0.37f, eased);
            DrawLine(new Vector2(Size.X * 0.52f - half, y), new Vector2(Size.X * 0.52f + half, y), new Color(AnimeVisualTheme.MoonWhite, 0.08f), 1.0f, true);
        }
        DrawLine(new Vector2(Size.X * 0.20f, Size.Y * 0.49f), new Vector2(Size.X * 0.84f, Size.Y * 0.49f), new Color(AnimeVisualTheme.PaleGold, 0.35f), 1.8f, true);
        DrawArc(new Vector2(Size.X * 0.52f, Size.Y * 0.52f), Size.X * 0.22f, MathF.PI, MathF.Tau, 80, new Color(AnimeVisualTheme.OathBlue, 0.055f), 4.0f, true);
        DrawArc(new Vector2(Size.X * 0.52f, Size.Y * 0.46f), Size.X * 0.18f, 0.0f, MathF.PI, 80, new Color(AnimeVisualTheme.PactViolet, 0.055f), 4.0f, true);
        DrawAtmosphere(false);
    }

    private void DrawAtmosphere(bool covered)
    {
        float alpha = covered ? 0.12f : 0.16f;
        for (int index = 0; index < 7; index++)
        {
            float radius = Size.Y * (0.12f + (index * 0.055f));
            DrawArc(new Vector2(Size.X * 0.18f, Size.Y * 0.52f), radius, -1.2f, 1.2f, 48, new Color(AnimeVisualTheme.OathBlue, alpha / (index + 1)), 3.0f, true);
            DrawArc(new Vector2(Size.X * 0.82f, Size.Y * 0.48f), radius, 1.9f, 4.3f, 48, new Color(AnimeVisualTheme.PactViolet, alpha / (index + 1)), 3.0f, true);
        }
    }
}

internal sealed partial class AnimeFactionSigil : Control
{
    private AnimeFaction _faction;

    internal void Configure(AnimeFaction faction)
    {
        _faction = faction;
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 center = Size * 0.5f;
        float radius = MathF.Min(Size.X, Size.Y) * 0.38f;
        Color accent = AnimeVisualTheme.FactionColor(_faction);
        DrawCircle(center, radius * 1.12f, new Color(accent, 0.09f));
        DrawArc(center, radius, 0.0f, MathF.Tau, 96, new Color(AnimeVisualTheme.OldGold, 0.76f), 3.0f, true);
        DrawArc(center, radius * 0.72f, 0.0f, MathF.Tau, 96, new Color(accent, 0.82f), 2.0f, true);
        if (_faction == AnimeFaction.Oathguard)
        {
            for (int index = 0; index < 12; index++)
            {
                float angle = index * MathF.Tau / 12.0f;
                Vector2 direction = new(MathF.Cos(angle), MathF.Sin(angle));
                DrawLine(center + (direction * radius * 0.78f), center + (direction * radius * 1.12f), new Color(AnimeVisualTheme.PaleGold, 0.68f), 2.0f, true);
            }
        }
        else
        {
            DrawLine(center - new Vector2(radius * 0.9f, radius * 0.72f), center + new Vector2(radius * 0.9f, radius * 0.72f), new Color(AnimeVisualTheme.PactCrimson, 0.95f), 7.0f, true);
            DrawLine(center - new Vector2(radius * 0.56f, radius), center + new Vector2(radius * 0.56f, radius), new Color(AnimeVisualTheme.PactViolet, 0.92f), 3.0f, true);
        }
    }
}

internal enum AnimePortraitOutcome
{
    Neutral,
    Winner,
    Loser,
}

internal sealed partial class AnimePortraitPreview : Control
{
    private AnimeFaction _faction;
    private string _name = string.Empty;
    private Texture2D? _texture;
    private bool _medallion;
    private AnimePortraitOutcome _outcome;
    private AnimeSliceMotionProfile _motionProfile = AnimeSliceMotionProfile.Disabled;
    private Vector2 _restPosition;
    private Vector2 _parallax;
    private double _elapsed;

    internal void Configure(
        AnimeFaction faction,
        string resourcePath,
        string name,
        bool medallion = false,
        AnimeSliceMotionProfile? motionProfile = null)
    {
        _faction = faction;
        _name = name;
        _texture = AnimeVisualAssetCatalog.TryLoad(resourcePath);
        _medallion = medallion;
        _motionProfile = motionProfile ?? AnimeSliceMotionProfile.Disabled;
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    internal void SetOutcome(AnimePortraitOutcome outcome)
    {
        _outcome = outcome;
        SelfModulate = outcome == AnimePortraitOutcome.Loser
            ? new Color(0.48f, 0.44f, 0.58f, 0.62f)
            : Colors.White;
        QueueRedraw();
    }

    public override void _Ready()
    {
        _restPosition = Position;
        PivotOffset = Size * 0.5f;
        bool animate = _motionProfile.Enabled && !_medallion;
        SetProcess(animate);
        if (!animate)
        {
            Position = _restPosition;
            Scale = Vector2.One;
        }
    }

    public override void _Process(double delta)
    {
        if (!_motionProfile.Enabled || _medallion)
        {
            return;
        }

        _elapsed += delta;
        Vector2 viewportSize = GetViewportRect().Size;
        Vector2 mouse = GetViewport().GetMousePosition();
        Vector2 normalized = viewportSize.X > 0.0f && viewportSize.Y > 0.0f
            ? new Vector2(
                Math.Clamp(((mouse.X / viewportSize.X) - 0.5f) * 2.0f, -1.0f, 1.0f),
                Math.Clamp(((mouse.Y / viewportSize.Y) - 0.5f) * 2.0f, -1.0f, 1.0f))
            : Vector2.Zero;
        Vector2 targetParallax = normalized * _motionProfile.ParallaxPixels;
        float smoothing = 1.0f - MathF.Exp(-(float)delta * 7.0f);
        _parallax = _parallax.Lerp(targetParallax, smoothing);

        float entryProgress = _motionProfile.EntryDurationSeconds <= 0.0f
            ? 1.0f
            : Math.Clamp((float)_elapsed / _motionProfile.EntryDurationSeconds, 0.0f, 1.0f);
        float easedEntry = 1.0f - MathF.Pow(1.0f - entryProgress, 3.0f);
        float entryOffset = (1.0f - easedEntry) * _motionProfile.EntryDistancePixels;
        float breathPhase = (float)(_elapsed * Math.Tau / _motionProfile.BreathPeriodSeconds);
        float breathScale = 1.0f + (MathF.Sin(breathPhase) * _motionProfile.BreathScaleAmplitude);

        Position = _restPosition + _parallax + new Vector2(0.0f, entryOffset);
        Scale = Vector2.One * breathScale;
    }

    public override void _Draw()
    {
        Rect2 rect = new(Vector2.Zero, Size);
        Color accent = AnimeVisualTheme.FactionColor(_faction);
        if (_outcome == AnimePortraitOutcome.Winner)
        {
            Vector2 glowCenter = new(Size.X * 0.50f, Size.Y * 0.48f);
            float glowRadius = MathF.Min(Size.X, Size.Y) * 0.43f;
            DrawCircle(glowCenter, glowRadius, new Color(AnimeVisualTheme.PaleGold, 0.10f));
            DrawArc(glowCenter, glowRadius * 0.92f, 0.0f, MathF.Tau, 96, new Color(AnimeVisualTheme.PaleGold, 0.74f), 4.0f, true);
            DrawArc(glowCenter, glowRadius * 1.06f, 0.0f, MathF.Tau, 96, new Color(accent, 0.38f), 2.0f, true);
        }
        if (_texture is not null)
        {
            if (_medallion)
            {
                float sourceSize = MathF.Min(_texture.GetWidth(), _texture.GetHeight() * 0.62f);
                var source = new Rect2(
                    (_texture.GetWidth() - sourceSize) * 0.5f,
                    _texture.GetHeight() * 0.055f,
                    sourceSize,
                    sourceSize);
                DrawTextureRectRegion(_texture, rect, source);
                DrawArc(rect.GetCenter(), MathF.Min(Size.X, Size.Y) * 0.47f, 0.0f, MathF.Tau, 64, new Color(accent, 0.96f), 2.5f, true);
            }
            else
            {
                float imageAspect = _texture.GetWidth() / (float)_texture.GetHeight();
                float targetAspect = rect.Size.X / rect.Size.Y;
                Vector2 fitted = targetAspect > imageAspect
                    ? new Vector2(rect.Size.Y * imageAspect, rect.Size.Y)
                    : new Vector2(rect.Size.X, rect.Size.X / imageAspect);
                Rect2 destination = new((rect.Size - fitted) * 0.5f, fitted);
                DrawTextureRect(_texture, destination, tile: false);
            }
        }
        else
        {
            Vector2 center = new(Size.X * 0.50f, Size.Y * 0.44f);
            float head = MathF.Min(Size.X, Size.Y) * 0.10f;
            DrawCircle(center - new Vector2(0.0f, Size.Y * 0.17f), head, new Color(AnimeVisualTheme.MoonWhite, 0.76f));
            Vector2[] silhouette =
            [
                new Vector2(Size.X * 0.12f, Size.Y * 0.92f),
                new Vector2(Size.X * 0.30f, Size.Y * 0.39f),
                new Vector2(Size.X * 0.50f, Size.Y * 0.29f),
                new Vector2(Size.X * 0.70f, Size.Y * 0.39f),
                new Vector2(Size.X * 0.88f, Size.Y * 0.92f),
            ];
            DrawColoredPolygon(silhouette, new Color(accent.Darkened(0.45f), 0.84f));
            DrawArc(center, MathF.Min(Size.X, Size.Y) * 0.34f, -2.75f, -0.39f, 64, new Color(AnimeVisualTheme.PaleGold, 0.46f), 4.0f, true);
        }
        if (!_medallion)
        {
            DrawString(AnimeVisualTheme.DisplayFont, new Vector2(8.0f, Size.Y - 14.0f), _name, HorizontalAlignment.Center, Size.X - 16.0f, 14, new Color(AnimeVisualTheme.MoonWhite, 0.72f));
        }
    }
}

internal sealed partial class AnimeLeaderCore : Control
{
    private AnimeFaction _faction;
    private string _name = string.Empty;
    private string _health = string.Empty;
    private bool _far;
    private AnimeSliceMotionProfile _motionProfile = AnimeSliceMotionProfile.Disabled;
    private Vector2 _restPosition;
    private double _hitElapsed = double.PositiveInfinity;
    private float _hitIntensity;

    internal void Configure(
        AnimeFaction faction,
        string name,
        string health,
        bool far,
        AnimeSliceMotionProfile? motionProfile = null)
    {
        _faction = faction;
        _name = name;
        _health = health;
        _far = far;
        _motionProfile = motionProfile ?? AnimeSliceMotionProfile.Disabled;
        MouseFilter = MouseFilterEnum.Ignore;
        QueueRedraw();
    }

    internal void TriggerHitPulse()
    {
        if (!_motionProfile.Enabled)
        {
            return;
        }
        _hitElapsed = 0.0;
        SetProcess(true);
    }

    public override void _Ready()
    {
        _restPosition = Position;
        PivotOffset = Size * 0.5f;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        _hitElapsed += delta;
        float duration = _motionProfile.HitDurationSeconds;
        float progress = duration <= 0.0f
            ? 1.0f
            : Math.Clamp((float)_hitElapsed / duration, 0.0f, 1.0f);
        if (progress >= 1.0f)
        {
            _hitIntensity = 0.0f;
            Position = _restPosition;
            Scale = Vector2.One;
            SelfModulate = Colors.White;
            QueueRedraw();
            SetProcess(false);
            return;
        }

        _hitIntensity = MathF.Sin(progress * MathF.PI);
        float shake = MathF.Sin(progress * MathF.PI * 7.0f) *
            _motionProfile.HitShakePixels * (1.0f - progress);
        Position = _restPosition + new Vector2(shake, 0.0f);
        Scale = Vector2.One * (1.0f + (_hitIntensity * _motionProfile.HitScaleAmplitude));
        SelfModulate = Colors.White.Lerp(new Color(1.0f, 0.58f, 0.66f), _hitIntensity * 0.55f);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Vector2 center = Size * 0.5f;
        Color accent = AnimeVisualTheme.FactionColor(_faction);
        if (_hitIntensity > 0.0f)
        {
            DrawCircle(center, 39.0f, new Color(AnimeVisualTheme.PactCrimson, _hitIntensity * 0.28f));
        }
        DrawCircle(center, 31.0f, new Color(AnimeVisualTheme.Ink, 0.86f));
        DrawArc(center, 31.0f, 0.0f, MathF.Tau, 64, new Color(accent, 0.92f), 3.0f, true);
        DrawCircle(center, 22.0f, new Color(accent.Darkened(0.35f), 0.92f));
        DrawString(AnimeVisualTheme.DisplayFont, center + new Vector2(-22.0f, 8.0f), _health, HorizontalAlignment.Center, 44.0f, 23, Colors.White);
    }
}
