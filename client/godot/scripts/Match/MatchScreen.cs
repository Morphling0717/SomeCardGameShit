using Godot;
using Scgs.Client;
using Scgs.GodotClient.UI;

namespace Scgs.GodotClient.Match;

public sealed partial class MatchScreen : Control
{
    private static readonly PackedScene SlotScene =
        GD.Load<PackedScene>("res://scenes/cards/SnapshotSlot.tscn");

    private ViewerRevealGate? _revealGate;
    private PlayerId _pendingViewer;
    private PassDeviceOverlay _privacyOverlay = null!;

    public event Action? ExitRequested;

    public event Action<MatchView>? FirstSnapshotPresented;

    public bool HasPresentedSnapshot { get; private set; }

    public bool IsPrivacyCoverVisible => _privacyOverlay.IsCovering;

    public int SnapshotRequestCount => _revealGate?.GetViewCallCount ?? 0;

    public int OpponentHandBackCount =>
        GetNodeOrNull<Container>("%OpponentHandBacks")?.GetChildCount() ?? 0;

    public override void _Ready()
    {
        _privacyOverlay = GetNode<PassDeviceOverlay>("%PassDeviceOverlay");
        _privacyOverlay.RevealRequested += OnRevealRequested;
        _privacyOverlay.ExitRequested += () => ExitRequested?.Invoke();
        GetNode<Button>("%ReturnButton").Pressed += () => ExitRequested?.Invoke();
    }

    public void Begin(IScgsGameSession session, PlayerId initialViewer)
    {
        _revealGate = new ViewerRevealGate(session);
        CoverFor(initialViewer);
        if (SnapshotRequestCount != 0)
        {
            throw new InvalidOperationException("A viewer snapshot was requested before the reveal gate opened.");
        }
    }

    public void RevealForCiSmoke()
    {
        if (!_privacyOverlay.IsCovering)
        {
            throw new InvalidOperationException("CI smoke must begin from the opaque privacy cover.");
        }

        _privacyOverlay.RequestRevealForSmoke();
    }

    public bool RenderedLabelsMatch(MatchView view)
    {
        if (view.Players.Length != 2)
        {
            return false;
        }

        PlayerView own = view.Players[(int)view.Viewer];
        PlayerView opponent = view.Players[(int)Other(view.Viewer)];
        return GetNode<Label>("%ViewerLabel").Text == $"观看者：{PlayerLabel(view.Viewer)}" &&
               GetNode<Label>("%PhaseLabel").Text == $"阶段：{PhaseLabel(view.Phase)}" &&
               GetNode<Label>("%RevisionLabel").Text == $"Revision {view.Revision}" &&
               GetNode<Label>("%MatchMetaLabel").Text ==
                   $"先手：{PlayerLabel(view.FirstPlayer)}  ·  当前行动：{PlayerLabel(view.ActivePlayer)}  ·  Seed：{view.RandomSeed}" &&
               GetNode<Label>("%OpponentSummary").Text == FormatPlayerSummary(opponent, "对手") &&
               GetNode<Label>("%OpponentZones").Text == FormatZoneSummary(opponent) &&
               GetNode<Label>("%OwnSummary").Text == FormatPlayerSummary(own, "己方") &&
               GetNode<Label>("%OwnZones").Text == FormatZoneSummary(own) &&
               GetNode<Label>("%PrivacyProof").Text ==
                   $"隐私校验：对手手牌仅显示数量 {opponent.HandCount}；安全快照中的对手 hand 数组为 {opponent.Hand.Length}。";
    }

    private void CoverFor(PlayerId viewer)
    {
        if (_revealGate is null)
        {
            throw new InvalidOperationException("The reveal gate has not been initialized.");
        }

        _pendingViewer = viewer;
        _revealGate.Cover(viewer);
        HasPresentedSnapshot = false;
        ClearSensitiveVisuals();
        _privacyOverlay.Cover(PlayerLabel(viewer));
    }

    private void OnRevealRequested()
    {
        if (_revealGate is null)
        {
            _privacyOverlay.KeepCoveredAfterFailure("比赛尚未初始化。");
            return;
        }

        try
        {
            // The explicit overlay event always precedes this GetView call.
            MatchView view = _revealGate.RevealAndGetView();
            Render(view);
            _privacyOverlay.CompleteReveal();
            HasPresentedSnapshot = true;
            FirstSnapshotPresented?.Invoke(view);
        }
        catch (Exception exception)
        {
            GD.PushError($"Failed to reveal viewer {_pendingViewer}: {exception}");
            ClearSensitiveVisuals();
            _revealGate.Cover(_pendingViewer);
            _privacyOverlay.KeepCoveredAfterFailure("无法读取安全快照，请返回主菜单后检查原生库日志。");
        }
    }

    private void Render(MatchView view)
    {
        if (view.Players.Length != 2)
        {
            throw new InvalidOperationException("A match snapshot must contain exactly two players.");
        }

        PlayerView own = view.Players[(int)view.Viewer];
        PlayerView opponent = view.Players[(int)Other(view.Viewer)];

        GetNode<Label>("%ViewerLabel").Text = $"观看者：{PlayerLabel(view.Viewer)}";
        GetNode<Label>("%PhaseLabel").Text = $"阶段：{PhaseLabel(view.Phase)}";
        GetNode<Label>("%RevisionLabel").Text = $"Revision {view.Revision}";
        GetNode<Label>("%MatchMetaLabel").Text =
            $"先手：{PlayerLabel(view.FirstPlayer)}  ·  当前行动：{PlayerLabel(view.ActivePlayer)}  ·  Seed：{view.RandomSeed}";

        GetNode<Label>("%OpponentSummary").Text = FormatPlayerSummary(opponent, "对手");
        GetNode<Label>("%OpponentZones").Text = FormatZoneSummary(opponent);
        GetNode<Label>("%OwnSummary").Text = FormatPlayerSummary(own, "己方");
        GetNode<Label>("%OwnZones").Text = FormatZoneSummary(own);

        PopulateSlots(GetNode<Container>("%OpponentTactics"), opponent.Tactics, "策略");
        PopulateSlots(GetNode<Container>("%OpponentUnits"), opponent.Units, "单位");
        PopulateSlots(GetNode<Container>("%OwnUnits"), own.Units, "单位");
        PopulateSlots(GetNode<Container>("%OwnTactics"), own.Tactics, "策略");
        PopulateOpponentHandBacks(GetNode<Container>("%OpponentHandBacks"), opponent.HandCount);
        PopulateHand(GetNode<Container>("%HandCards"), own.Hand);

        GetNode<Label>("%PrivacyProof").Text =
            $"隐私校验：对手手牌仅显示数量 {opponent.HandCount}；安全快照中的对手 hand 数组为 {opponent.Hand.Length}。";
    }

    private static string FormatPlayerSummary(PlayerView player, string relation)
    {
        return $"{relation} · {PlayerLabel(player.Player)}    " +
               $"生命 {player.LeaderHealth}/{player.MaximumLeaderHealth}    " +
               $"当前 PP {player.CurrentPp} / 容量 {player.PpCapacity}    " +
               $"裂痕 {player.Cracks}    进化能量 {player.EvolutionEnergy}";
    }

    private static string FormatZoneSummary(PlayerView player)
    {
        return $"手牌 {player.HandCount} · 牌组 {player.DeckCount} · " +
               $"{FormatPublicZone("战备", player.Standby)} · " +
               $"{FormatPublicZone("墓地", player.Graveyard)} · " +
               FormatPublicZone("封存", player.Archive);
    }

    private static string FormatPublicZone(string label, IReadOnlyList<CardView> cards) =>
        cards.Count == 0
            ? $"{label} 0"
            : $"{label} {cards.Count} [{string.Join("、", cards.Select(card => card.Name))}]";

    private static void PopulateSlots(Container container, IReadOnlyList<CardView?> cards, string zoneName)
    {
        FreeChildren(container);
        for (int index = 0; index < cards.Count; index++)
        {
            SnapshotSlot slot = SlotScene.Instantiate<SnapshotSlot>();
            container.AddChild(slot);
            if (cards[index] is { } card)
            {
                slot.ShowCard(card, zoneName, index);
            }
            else
            {
                slot.ShowEmpty(zoneName, index);
            }
        }
    }

    private static void PopulateHand(Container container, IReadOnlyList<CardView> cards)
    {
        FreeChildren(container);
        for (int index = 0; index < cards.Count; index++)
        {
            SnapshotSlot slot = SlotScene.Instantiate<SnapshotSlot>();
            slot.CustomMinimumSize = new Vector2(190, 76);
            container.AddChild(slot);
            slot.ShowCard(cards[index], "手牌", index);
        }

        if (cards.Count == 0)
        {
            var empty = new Label { Text = "当前观看者没有可显示的手牌。" };
            container.AddChild(empty);
        }
    }

    private static void PopulateOpponentHandBacks(Container container, ulong handCount)
    {
        FreeChildren(container);
        for (ulong index = 0; index < handCount; index++)
        {
            // Every back is deliberately identical: no definition, instance,
            // ordinal text, tooltip, metadata, or stable identity is attached.
            container.AddChild(new ColorRect
            {
                Color = new Color(0.12f, 0.31f, 0.39f, 1.0f),
                CustomMinimumSize = new Vector2(24, 32),
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
    }

    private void ClearSensitiveVisuals()
    {
        foreach (string path in new[]
                 {
                     "%OpponentHandBacks", "%OpponentTactics", "%OpponentUnits",
                     "%OwnUnits", "%OwnTactics", "%HandCards",
                 })
        {
            FreeChildren(GetNode<Container>(path));
        }

        foreach (string path in new[] { "%OpponentSummary", "%OpponentZones", "%OwnSummary", "%OwnZones", "%PrivacyProof" })
        {
            GetNode<Label>(path).Text = string.Empty;
        }
    }

    private static void FreeChildren(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            child.Free();
        }
    }

    private static PlayerId Other(PlayerId player) =>
        player == PlayerId.Player0 ? PlayerId.Player1 : PlayerId.Player0;

    private static string PlayerLabel(PlayerId player) =>
        player == PlayerId.Player0 ? "玩家 0" : "玩家 1";

    private static string PhaseLabel(MatchPhase phase) => phase switch
    {
        MatchPhase.NotStarted => "未开始",
        MatchPhase.Mulligan => "调度",
        MatchPhase.Action => "行动",
        MatchPhase.Reaction => "响应",
        MatchPhase.Finished => "已结束",
        _ => $"未知（{(uint)phase}）",
    };
}
