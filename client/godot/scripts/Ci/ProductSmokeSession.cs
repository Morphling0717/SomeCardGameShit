// SPDX-License-Identifier: GPL-3.0-or-later
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Ci;

/// <summary>
/// Observes the actual product session without issuing extra reads or commands.
/// Only aggregate, identity-free evidence survives each call. This is not an agent.
/// </summary>
internal sealed class ProductSmokeSession : V05.IScgsV05GameSession
{
    private readonly V05.IScgsV05GameSession inner;
    private readonly HashSet<ulong> endedSequences = [];
    private V05.PlayerId? allowedViewer;
    private int armedFrames;
    private int lastSubmittedInput;
    private ulong lastEventSequence;
    private ulong endedSequence;
    private bool lastViewHadReaction;
    private bool lastViewHadChoice;

    internal ProductSmokeSession(V05.IScgsV05GameSession inner)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        IsRealNativeSession = inner is V05.ScgsV05GameSession;
    }

    internal bool IsRealNativeSession { get; }
    internal int[] ActionCounts { get; } = new int[14];
    internal int InputSerial { get; set; }
    internal int PrematureViewReads { get; private set; }
    internal int UnauthorizedPrivateQueries { get; private set; }
    internal int SchedulingQueries { get; private set; }
    internal int UnattributedCommands { get; private set; }
    internal int EngineFailures { get; private set; }
    internal int MinimumPublicFrames { get; private set; } = int.MaxValue;
    internal int PrivateLeaks { get; private set; }
    internal int CoveredSamples { get; set; }
    internal int ResolvingSamples { get; set; }
    internal int DisposedCount { get; private set; }
    internal ulong Revision { get; private set; }
    internal V05.GameResult Result { get; private set; }
    internal int MatchEndedCount => endedSequences.Count;
    internal bool MatchEndedLast => endedSequence != 0 && endedSequence == lastEventSequence;
    internal int Commands => ActionCounts.Sum();
    internal int SubmitAttempts { get; private set; }
    internal int NativeCallCount { get; private set; }
    internal int ViewerReadCount { get; private set; }
    internal int PrivateQueryCount { get; private set; }
    internal int ReactionSurrenders { get; private set; }
    internal int ChoiceSurrenders { get; private set; }

    internal void AuthorizeReveal(V05.PlayerId viewer) => allowedViewer = viewer;
    internal void RevokeViewerAccess() => allowedViewer = null;
    internal void BeforeSubmit(int actualPublicFrames) => armedFrames = actualPublicFrames;

    public V05.EngineStatus Start() { ++NativeCallCount; return inner.Start(); }

    public V05.MatchView GetView(V05.PlayerId viewer)
    {
        ++NativeCallCount;
        ++ViewerReadCount;
        CheckViewer(viewer);
        V05.MatchView view = inner.GetView(viewer);
        Revision = view.Revision;
        Result = view.Result;
        lastViewHadReaction = view.Reaction.Pending;
        lastViewHadChoice = view.PendingChoice.Pending;
        foreach (V05.PlayerView player in view.Players)
        {
            if (player.Player == viewer) continue;
            if (player.Hand.Length != 0) ++PrivateLeaks;
            foreach (V05.CardView card in player.Tactics.OfType<V05.CardView>())
            {
                if (card.FaceDown && (card.InstanceId is not null ||
                    card.DesignId is not null || card.Name.Length != 0)) ++PrivateLeaks;
            }
        }
        return view;
    }

    public V05.EngineStatus SubmitCommand(V05.GameCommandRequest command)
    {
        ++SubmitAttempts;
        ++NativeCallCount;
        // The UI's resolving gate is the only legitimate route to this call.
        if (InputSerial <= lastSubmittedInput) ++UnattributedCommands;
        MinimumPublicFrames = Math.Min(MinimumPublicFrames, armedFrames);
        armedFrames = 0;
        V05.EngineStatus result = inner.SubmitCommand(command);
        if (result.IsSuccess)
        {
            int kind = checked((int)command.Action);
            if (kind < 0 || kind >= ActionCounts.Length)
                throw new InvalidOperationException("Unknown product action in smoke observer.");
            ++ActionCounts[kind];
            if (command.Action == V05.ActionKind.Surrender)
            {
                if (lastViewHadChoice) ++ChoiceSurrenders;
                else if (lastViewHadReaction) ++ReactionSurrenders;
            }
            lastSubmittedInput = InputSerial;
            // Refresh of the SAME operator is permitted after submission; a new
            // operator still requires the real reveal-button callback.
            allowedViewer = command.Player;
        }
        else ++EngineFailures;
        return result;
    }

    public V05.EventBatch ReadEvents(V05.PlayerId viewer, ulong afterSequence)
    {
        ++NativeCallCount;
        ++ViewerReadCount;
        CheckViewer(viewer);
        return Observe(inner.ReadEvents(viewer, afterSequence));
    }

    public V05.EventBatch ReadNewEvents(V05.PlayerId viewer)
    {
        ++NativeCallCount;
        ++ViewerReadCount;
        CheckViewer(viewer);
        return Observe(inner.ReadNewEvents(viewer));
    }

    private V05.EventBatch Observe(V05.EventBatch batch)
    {
        foreach (V05.GameEventView item in batch.Events)
        {
            lastEventSequence = Math.Max(lastEventSequence, item.Sequence);
            if (item.Type == V05.EventType.MatchEnded)
            {
                endedSequences.Add(item.Sequence);
                endedSequence = item.Sequence;
            }
            if (item.HiddenCard && (item.Card is not null || item.DesignId is not null)) ++PrivateLeaks;
        }
        return batch;
    }

    private void CheckViewer(V05.PlayerId viewer)
    {
        if (viewer != allowedViewer) ++PrematureViewReads;
    }

    private void CheckPrivateQuery(V05.PlayerId viewer)
    {
        ++PrivateQueryCount;
        ++NativeCallCount;
        if (viewer != allowedViewer) ++UnauthorizedPrivateQueries;
    }
    public V05.LegalActionsResult ListLegalActions(V05.ActionQueryRequest query)
    {
        CheckPrivateQuery(query.Player);
        return inner.ListLegalActions(query);
    }
    public V05.ValidTargetsResult ListValidTargets(V05.ActionQueryRequest query)
    {
        CheckPrivateQuery(query.Player);
        return inner.ListValidTargets(query);
    }
    public V05.ValidSlotsResult ListValidSlots(V05.ActionQueryRequest query)
    {
        CheckPrivateQuery(query.Player);
        return inner.ListValidSlots(query);
    }
    public V05.ValidDonorsResult ListValidDonors(V05.ActionQueryRequest query)
    {
        CheckPrivateQuery(query.Player);
        return inner.ListValidDonors(query);
    }
    public V05.PaymentResult PreviewPayment(V05.GameCommandRequest command)
    {
        CheckPrivateQuery(command.Player);
        return inner.PreviewPayment(command);
    }
    public V05.ReactionAndChoiceResult GetReactionContext(V05.PlayerId viewer)
    {
        ++SchedulingQueries;
        ++NativeCallCount;
        V05.ReactionAndChoiceResult context = inner.GetReactionContext(viewer);
        // Public scheduling information can be queried while covered, but it
        // must not smuggle the next viewer's private candidates along with it.
        if (viewer != allowedViewer && (context.Reaction.EligibleTraps.Length != 0 ||
            context.PendingChoice.ChoiceId is not null || context.PendingChoice.Options.Length != 0))
            ++PrivateLeaks;
        return context;
    }
    public ulong GetEventCursor(V05.PlayerId viewer) => inner.GetEventCursor(viewer);

    public void Dispose()
    {
        if (DisposedCount != 0) return;
        inner.Dispose();
        ++DisposedCount;
        allowedViewer = null;
    }
}
