// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;

namespace Scgs.Hotseat;

public enum HotseatUiMode
{
    Covered,
    MulliganSelecting,
    MulliganReview,
    Action,
    Reaction,
    Finished,
    Faulted,
    Disposed,
}

public enum HotseatCoverReason
{
    InitialReveal,
    PassingDevice,
    ResolvingCommand,
}

public readonly record struct HotseatEventCursors(ulong Player0, ulong Player1)
{
    public ulong For(PlayerId player) => player switch
    {
        PlayerId.Player0 => Player0,
        PlayerId.Player1 => Player1,
        _ => throw new ArgumentOutOfRangeException(
            nameof(player),
            player,
            "Unsupported player value."),
    };

    internal HotseatEventCursors With(PlayerId player, ulong value) => player switch
    {
        PlayerId.Player0 => this with { Player0 = value },
        PlayerId.Player1 => this with { Player1 = value },
        _ => throw new ArgumentOutOfRangeException(
            nameof(player),
            player,
            "Unsupported player value."),
    };
}

public sealed record HotseatActionSelection
{
    public ActionKind? Action { get; init; }

    public ulong? Source { get; init; }

    public bool HasTarget { get; init; }

    public Target? Target { get; init; }

    public bool HasSlot { get; init; }

    public ulong? Slot { get; init; }

    public bool HasDonor { get; init; }

    public ulong? Donor { get; init; }

    public bool HasAdvanceChoice { get; init; }

    public bool UseAdvance { get; init; }

    public static HotseatActionSelection Empty { get; } = new();
}

public sealed record HotseatCandidateOptions
{
    internal HotseatCandidateOptions(IReadOnlyList<LegalAction> actions)
    {
        Actions = Freeze(actions);
        ActionKinds = Freeze(actions.Select(item => item.Command.Action).Distinct());
        Sources = Freeze(actions.Select(item => item.Command.Source).Distinct());
        Targets = FreezeDistinct(actions.Select(item => item.Command.Target));
        Slots = Freeze(actions.Select(item => item.Command.Slot).Distinct());
        Donors = Freeze(actions.Select(item => item.Command.ComponentDonor).Distinct());
        AdvanceChoices = Freeze(actions.Select(item => item.Command.UseAdvance).Distinct());
    }

    public IReadOnlyList<LegalAction> Actions { get; }

    public IReadOnlyList<ActionKind> ActionKinds { get; }

    public IReadOnlyList<ulong> Sources { get; }

    public IReadOnlyList<Target?> Targets { get; }

    public IReadOnlyList<ulong?> Slots { get; }

    public IReadOnlyList<ulong?> Donors { get; }

    public IReadOnlyList<bool> AdvanceChoices { get; }

    internal static HotseatCandidateOptions Empty { get; } = new(Array.Empty<LegalAction>());

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());

    private static IReadOnlyList<Target?> FreezeDistinct(IEnumerable<Target?> values)
    {
        var distinct = new List<Target?>();
        foreach (Target? value in values)
        {
            if (!distinct.Any(existing => Equals(existing, value)))
            {
                distinct.Add(value);
            }
        }

        return Array.AsReadOnly(distinct.ToArray());
    }
}

public sealed record HotseatUiState
{
    internal HotseatUiState(
        HotseatUiMode mode,
        HotseatCoverReason? coverReason,
        PlayerId? viewer,
        PlayerId? awaitingPlayer,
        MatchView? snapshot,
        IReadOnlyList<LegalAction> legalActions,
        IReadOnlyList<LegalAction> candidateActions,
        HotseatActionSelection selection,
        IReadOnlyList<ulong> mulliganCards,
        LegalAction? selectedAction,
        IReadOnlyList<GameEventView> events,
        IReadOnlyList<GameEventView> pendingEvents,
        ulong? pendingEventLastSequence,
        HotseatEventCursors eventCursors,
        uint? lastEngineCode,
        string? failureText,
        bool commandPrepared)
    {
        Mode = mode;
        CoverReason = coverReason;
        Viewer = viewer;
        AwaitingPlayer = awaitingPlayer;
        Snapshot = snapshot;
        LegalActions = Freeze(legalActions);
        Selection = selection;
        MulliganCards = Freeze(mulliganCards);
        SelectedAction = selectedAction;
        Events = Freeze(events);
        PendingEvents = Freeze(pendingEvents);
        PendingEventLastSequence = pendingEventLastSequence;
        EventCursors = eventCursors;
        LastEngineCode = lastEngineCode;
        FailureText = failureText;
        CommandPrepared = commandPrepared;
        CandidateOptions = new HotseatCandidateOptions(candidateActions);
    }

    public HotseatUiMode Mode { get; }

    public HotseatCoverReason? CoverReason { get; }

    public PlayerId? Viewer { get; }

    public PlayerId? AwaitingPlayer { get; }

    public MatchView? Snapshot { get; }

    public IReadOnlyList<LegalAction> LegalActions { get; }

    public HotseatCandidateOptions CandidateOptions { get; }

    public HotseatActionSelection Selection { get; }

    public IReadOnlyList<ulong> MulliganCards { get; }

    public LegalAction? SelectedAction { get; }

    public IReadOnlyList<GameEventView> Events { get; }

    public IReadOnlyList<GameEventView> PendingEvents { get; }

    public ulong? PendingEventLastSequence { get; }

    public HotseatEventCursors EventCursors { get; }

    public uint? LastEngineCode { get; }

    public string? FailureText { get; }

    public bool CommandPrepared { get; }

    public bool IsCovered => Mode == HotseatUiMode.Covered;

    public bool CanConfirm => SelectedAction is not null && !CommandPrepared;

    public bool HasUnacknowledgedEvents => PendingEventLastSequence.HasValue;

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}

public sealed class HotseatStateChangedEventArgs : EventArgs
{
    public HotseatStateChangedEventArgs(HotseatUiState state) => State = state;

    public HotseatUiState State { get; }
}
