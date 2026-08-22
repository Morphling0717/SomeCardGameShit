// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.Client;
using Scgs.GodotClient.Match;
using Scgs.Hotseat;

namespace Scgs.GodotClient.Ci;

internal sealed record Gate3CSmokeOutcome(
    MatchView FinalView,
    int Steps,
    int Turns,
    IReadOnlyList<ActionKind> ActionKinds,
    int Covers,
    int Reveals,
    int PrematureViewerCalls,
    int DisposedSessions,
    bool SignalE2e,
    bool ClickDragCanonicalParity,
    bool SelectionCommitWithoutConfirmation,
    int ResolvingPublicFrames,
    int ResolvingPrivateLeaks,
    int Restarts,
    int SurrenderTerminals);

internal sealed class Gate3CFullMatchSmoke
{
    private const int MaximumCommandsPerMatch = 1200;
    private const int MaximumFramesPerTransition = 600;

    private static readonly ActionKind[] CoveragePriority =
    [
        ActionKind.PlayTactic,
        ActionKind.CastSpell,
        ActionKind.PlayUnit,
        ActionKind.Deploy,
        ActionKind.Evolve,
        ActionKind.Attack,
        ActionKind.EndTurn,
    ];

    private readonly MatchScreen match;
    private readonly Func<Task> nextFrame;
    private readonly string? resolvingScreenshotPath;
    private readonly HashSet<ActionKind> submittedKinds = [];
    private readonly HashSet<(PlayerId Player, int OwnTurn)> reactionProbeTurns = [];
    private int endTurnCount;
    private bool parityProbed;
    private bool layoutProbed;
    private bool privacySentinelProbed;

    internal Gate3CFullMatchSmoke(
        MatchScreen match,
        Func<Task> nextFrame,
        string? resolvingScreenshotPath = null)
    {
        this.match = match ?? throw new ArgumentNullException(nameof(match));
        this.nextFrame = nextFrame ?? throw new ArgumentNullException(nameof(nextFrame));
        this.resolvingScreenshotPath = resolvingScreenshotPath;
    }

    internal async Task<Gate3CSmokeOutcome> RunAsync()
    {
        while (match.CiSuccessfulSubmissionCount < MaximumCommandsPerMatch)
        {
            ThrowIfPrivacyViolated();
            HotseatUiState state = match.CiState;
            switch (state.Mode)
            {
                case HotseatUiMode.Covered:
                    await RevealCoveredViewerAsync(state);
                    break;
                case HotseatUiMode.Resolving:
                    await WaitUntilAsync(
                        () => match.CiState.Mode != HotseatUiMode.Resolving,
                        "the public resolving projection did not advance");
                    break;
                case HotseatUiMode.MulliganSelecting:
                    await SubmitMulliganAsync(state);
                    break;
                case HotseatUiMode.MulliganReview:
                    await CompleteMulliganReviewAsync(state);
                    break;
                case HotseatUiMode.Action:
                case HotseatUiMode.Reaction:
                    await SubmitNormalCommandAsync(state);
                    break;
                case HotseatUiMode.Finished:
                    return await CompleteAsync(state);
                case HotseatUiMode.Faulted:
                    throw new InvalidOperationException(
                        $"The hot-seat UI entered its controlled error state: {state.FailureText}");
                case HotseatUiMode.Disposed:
                    throw new InvalidOperationException("The match session was disposed before reaching a result.");
                default:
                    throw new InvalidOperationException($"Unsupported CI UI state: {state.Mode}.");
            }
        }

        throw new InvalidOperationException(
            $"The deterministic match exceeded {MaximumCommandsPerMatch} successful commands.");
    }

    private async Task RevealCoveredViewerAsync(HotseatUiState covered)
    {
        if (!covered.AwaitingPlayer.HasValue)
        {
            throw new InvalidOperationException(
                "Gate 3C Covered state must name the viewer awaiting reveal.");
        }

        if (!match.IsPrivacyCoverVisible || match.HasPresentedSnapshot)
        {
            throw new InvalidOperationException(
                "A player handoff did not clear private nodes behind an opaque cover.");
        }

        int revealCount = match.CiRevealRequestCount;
        int snapshotCount = match.SnapshotRequestCount;
        match.RevealForCiSmoke();
        await WaitUntilAsync(
            () => match.CiState.Mode != HotseatUiMode.Covered &&
                  match.HasPresentedSnapshot &&
                  !match.IsPrivacyCoverVisible,
            "the explicitly revealed viewer was not rendered");

        if (match.CiRevealRequestCount != revealCount + 1 ||
            match.SnapshotRequestCount <= snapshotCount)
        {
            throw new InvalidOperationException(
                "An explicit reveal did not produce exactly one reveal request and a fresh snapshot.");
        }

        await WaitForRenderedEventsAsync();
        ValidateVisibleSnapshot(match.CiState);
    }

    private async Task SubmitMulliganAsync(HotseatUiState state)
    {
        ValidateVisibleSnapshot(state);
        await WaitForRenderedEventsAsync();
        state = match.CiState;
        if (!match.CiMulliganVisible || state.SelectedAction?.Command is not { } command ||
            command.Action != ActionKind.Mulligan || command.MulliganCards.Count != 0)
        {
            throw new InvalidOperationException(
                "The mulligan UI did not present the exact empty replacement command.");
        }

        await SubmitAndWaitAsync(
            ActionKind.Mulligan,
            match.ConfirmCurrentSelectionForCi);
    }

    private async Task CompleteMulliganReviewAsync(HotseatUiState state)
    {
        ValidateVisibleSnapshot(state);
        await WaitForRenderedEventsAsync();
        if (!match.CiMulliganVisible)
        {
            throw new InvalidOperationException("The replacement hand review was not visibly presented.");
        }

        match.CompleteMulliganReviewForCi();
        await nextFrame();
    }

    private async Task SubmitNormalCommandAsync(HotseatUiState state)
    {
        ValidateVisibleSnapshot(state);
        await WaitForRenderedEventsAsync();
        state = match.CiState;
        if (!layoutProbed)
        {
            if (!match.VerifyDockCollapseForCi() ||
                !MatchScreen.ValidateReferenceLayoutForCi(1600, 900) ||
                !MatchScreen.ValidateReferenceLayoutForCi(1280, 720))
            {
                throw new InvalidOperationException(
                    "The collapsible dock or reference-resolution layout contract failed.");
            }
            layoutProbed = true;
        }
        MatchView view = state.Snapshot ??
            throw new InvalidOperationException("A command state is missing its safe snapshot.");
        LegalAction selected = SelectCommand(state.LegalActions, view, view.Viewer) ??
            throw new InvalidOperationException(
                $"The selector found no non-surrender command at revision {view.Revision}.");

        if (!parityProbed)
        {
            LegalAction[] dragCandidates = state.LegalActions.Where(action =>
                action.Command.Source != 0 &&
                action.Command.ComponentDonor is null &&
                (action.Command.Target is { Kind: TargetKind.Unit } ||
                 action.Command.Slot.HasValue)).ToArray();
            LegalAction? probe = dragCandidates.FirstOrDefault(action =>
                                     action.Command.Target is { Kind: TargetKind.Unit } &&
                                     action.Command.Slot.HasValue) ??
                                 dragCandidates.FirstOrDefault();
            if (probe is not null)
            {
                if (!match.VerifyCancelledDragNoSideEffectsForCi(probe) ||
                    !match.VerifyClickDragParityForCi(probe))
                {
                    throw new InvalidOperationException(
                        "Click/drag parity or cancelled-drag side-effect probe failed.");
                }
                parityProbed = true;
                state = match.CiState;
                selected = SelectCommand(state.LegalActions, state.Snapshot!, state.Snapshot!.Viewer) ??
                    throw new InvalidOperationException("No command remained after the parity probe.");
            }
        }

        LegalAction captured = selected;
        if (!privacySentinelProbed)
        {
            match.ArmResolvingPrivacySentinelForCi(resolvingScreenshotPath);
            privacySentinelProbed = true;
        }
        await SubmitAndWaitAsync(
            selected.Command.Action,
            () => match.SubmitLegalActionThroughSignalsForCi(captured));
    }

    private async Task SubmitAndWaitAsync(ActionKind expectedAction, Action submitThroughUi)
    {
        int before = match.CiSubmissionCount;
        submitThroughUi();
        if (match.IsPrivacyCoverVisible || match.CiState.Mode != HotseatUiMode.Resolving ||
            match.CiState.PublicBoard is null || match.CiResolvingPrivateLeakCount != 0)
        {
            throw new InvalidOperationException(
                $"{expectedAction} did not enter a leak-free public resolving projection.");
        }

        // The native command may run only after the public projection has
        // survived two complete process frames.
        await nextFrame();
        if (match.CiSubmissionCount != before)
        {
            throw new InvalidOperationException(
                $"{expectedAction} was submitted before the public board survived a complete frame.");
        }

        await WaitUntilAsync(
            () => match.CiSubmissionCount == before + 1,
            $"the deferred {expectedAction} command was not submitted");
        EngineStatus status = match.CiLastSubmissionStatus ??
            throw new InvalidOperationException("The UI did not retain the latest engine status.");
        if (!status.IsSuccess || match.CiSuccessfulSubmissionCount != before + 1)
        {
            throw new InvalidOperationException(
                $"The enumerated {expectedAction} command failed: {status.Message}");
        }

        submittedKinds.Add(expectedAction);
        if (expectedAction == ActionKind.EndTurn)
        {
            endTurnCount++;
        }

        await WaitUntilAsync(
            () => match.CiState.Mode == HotseatUiMode.Covered || match.HasPresentedSnapshot,
            $"the post-{expectedAction} UI state was not rendered");
    }

    private async Task<Gate3CSmokeOutcome> CompleteAsync(HotseatUiState state)
    {
        await WaitUntilAsync(
            () => match.HasPresentedSnapshot && match.CiResultVisible,
            "the terminal result overlay was not visibly presented");
        await WaitForRenderedEventsAsync();
        state = match.CiState;
        MatchView finalView = state.Snapshot ??
            throw new InvalidOperationException("The terminal UI state is missing its safe snapshot.");
        if (finalView.Phase != MatchPhase.Finished || finalView.Result == GameResult.Ongoing)
        {
            throw new InvalidOperationException("The full-match smoke stopped before a terminal result.");
        }

        ActionKind[] expectedKinds = Enum.GetValues<ActionKind>()
            .Where(action => action != ActionKind.Surrender)
            .ToArray();
        if (!submittedKinds.SetEquals(expectedKinds) ||
            !match.CiSawReaction ||
            match.CiPassingDeviceCoverCount == 0 ||
            match.CiRevealRequestCount < 2 ||
            match.CiEventAcknowledgeCount == 0 ||
            endTurnCount == 0 ||
            !parityProbed ||
            !layoutProbed ||
            !privacySentinelProbed ||
            !match.CiPrivacySentinelVerified ||
            (resolvingScreenshotPath is not null && !match.CiResolvingScreenshotCaptured) ||
            !match.CiCancelledDragNoSideEffects ||
            !match.CiSourceAdjacentPanelVerified ||
            !match.CiSignalE2e ||
            !match.CiClickDragCanonicalParity ||
            !match.CiSelectionCommitWithoutConfirmation ||
            match.CiMinimumResolvingFrames < 2 ||
            match.CiResolvingPrivateLeakCount != 0)
        {
            throw new InvalidOperationException(
                "The deterministic UI match did not exercise every non-surrender action, " +
                "reaction, rendered events, and hot-seat handoff.");
        }

        ThrowIfPrivacyViolated();
        if (match.CiRevealRequestCount > match.CiCoverPresentationCount)
        {
            throw new InvalidOperationException("Reveal count exceeded the number of opaque covers.");
        }

        int steps = match.CiSuccessfulSubmissionCount;
        int covers = match.CiCoverPresentationCount;
        int reveals = match.CiRevealRequestCount;
        int premature = match.CiPrematureViewerCallCount;
        ActionKind[] actionKinds = submittedKinds.OrderBy(action => action).ToArray();
        return new Gate3CSmokeOutcome(
            finalView,
            steps,
            endTurnCount,
            actionKinds,
            covers,
            reveals,
            premature,
            0,
            match.CiSignalE2e,
            match.CiClickDragCanonicalParity,
            match.CiSelectionCommitWithoutConfirmation,
            match.CiMinimumResolvingFrames,
            match.CiResolvingPrivateLeakCount,
            0,
            0);
    }

    private async Task WaitForRenderedEventsAsync()
    {
        if (!match.CiState.HasUnacknowledgedEvents)
        {
            return;
        }

        int acknowledgements = match.CiEventAcknowledgeCount;
        await WaitUntilAsync(
            () => !match.CiState.HasUnacknowledgedEvents,
            "the rendered event batch was not acknowledged");
        if (match.CiEventAcknowledgeCount <= acknowledgements)
        {
            throw new InvalidOperationException(
                "The event cursor advanced without MatchScreen recording a rendered acknowledgement.");
        }

        await nextFrame();
    }

    private async Task WaitUntilAsync(Func<bool> predicate, string failure)
    {
        for (int frame = 0; frame < MaximumFramesPerTransition; frame++)
        {
            if (predicate())
            {
                return;
            }

            HotseatUiMode mode = match.CiState.Mode;
            if (mode == HotseatUiMode.Faulted)
            {
                throw new InvalidOperationException(
                    $"{failure}; the UI entered its controlled error state: {match.CiState.FailureText}");
            }

            await nextFrame();
        }

        throw new TimeoutException(
            $"Timed out after {MaximumFramesPerTransition} frames while waiting for {failure}.");
    }

    private void ValidateVisibleSnapshot(HotseatUiState state)
    {
        if (state.IsCovered || !state.Viewer.HasValue || state.Snapshot is null ||
            !match.HasPresentedSnapshot || match.IsPrivacyCoverVisible)
        {
            throw new InvalidOperationException("The CI driver observed private state before visible reveal.");
        }

        MatchView view = state.Snapshot;
        if (view.Viewer != state.Viewer.Value || view.Players.Length != 2)
        {
            throw new InvalidOperationException("The rendered snapshot viewer shape is inconsistent.");
        }

        PlayerView own = view.Players[(int)view.Viewer];
        PlayerView opponent = view.Players[(int)Opponent(view.Viewer)];
        if ((ulong)own.Hand.Length != own.HandCount || opponent.Hand.Length != 0 ||
            (ulong)match.OpponentHandBackCount != opponent.HandCount ||
            !match.RenderedLabelsMatch(view))
        {
            throw new InvalidOperationException(
                "Rendered labels, hand backs, or viewer-scoped hand data disagree with the safe DTO.");
        }

        foreach (CardView? tactic in opponent.Tactics)
        {
            if (tactic is { FaceDown: true } &&
                (tactic.InstanceId.HasValue || tactic.DefinitionId.HasValue ||
                 tactic.Definition is not null || tactic.Kind.HasValue || tactic.Name.Length != 0))
            {
                throw new InvalidOperationException("A rendered opponent trap exposes stable identity.");
            }
        }

        ThrowIfPrivacyViolated();
    }

    private void ThrowIfPrivacyViolated()
    {
        if (match.CiPrematureViewerCallCount != 0)
        {
            throw new InvalidOperationException(
                "A viewer-scoped native call occurred before its explicit privacy reveal.");
        }
    }

    private LegalAction? SelectCommand(
        IReadOnlyList<LegalAction> actions,
        MatchView view,
        PlayerId actor)
    {
        if (view.Phase == MatchPhase.Reaction)
        {
            ActionKind first = submittedKinds.Contains(ActionKind.ActivateTrap)
                ? ActionKind.PassReaction
                : ActionKind.ActivateTrap;
            return FindAction(actions, first) ??
                   FindAction(actions, ActionKind.ActivateTrap) ??
                   FindAction(actions, ActionKind.PassReaction);
        }

        foreach (ActionKind kind in CoveragePriority)
        {
            if (submittedKinds.Contains(kind))
            {
                continue;
            }

            LegalAction? unseen = kind == ActionKind.Attack
                ? FindUnitAttack(actions) ?? FindLeaderAttack(actions)
                : FindAction(actions, kind);
            if (unseen is not null)
            {
                return unseen;
            }
        }

        bool needsReactionCoverage =
            !submittedKinds.Contains(ActionKind.ActivateTrap) ||
            !submittedKinds.Contains(ActionKind.PassReaction);
        PlayerId opponent = Opponent(actor);
        bool opponentHasFaceDownTactic = view.Players[(int)opponent].Tactics.Any(
            card => card is { FaceDown: true });
        int ownTurn = view.Players[(int)actor].OwnTurnNumber;
        if (needsReactionCoverage && opponentHasFaceDownTactic &&
            reactionProbeTurns.Add((actor, ownTurn)))
        {
            LegalAction? probe = FindLeaderAttack(actions);
            if (probe is not null)
            {
                return probe;
            }
        }

        bool needsOrdinaryCoverage = Enum.GetValues<ActionKind>()
            .Where(action => action != ActionKind.Surrender)
            .Any(action => !submittedKinds.Contains(action));
        if (needsOrdinaryCoverage)
        {
            return FindAction(actions, ActionKind.PlayTactic) ??
                   FindAction(actions, ActionKind.CastSpell) ??
                   FindAction(actions, ActionKind.Deploy) ??
                   FindAction(actions, ActionKind.Evolve) ??
                   FindAction(actions, ActionKind.PlayUnit) ??
                   FindUnitAttack(actions) ??
                   FindAction(actions, ActionKind.EndTurn);
        }

        return FindLeaderAttack(actions) ??
               FindUnitAttack(actions) ??
               FindAction(actions, ActionKind.Evolve) ??
               FindAction(actions, ActionKind.PlayUnit) ??
               FindAction(actions, ActionKind.CastSpell) ??
               FindAction(actions, ActionKind.Deploy) ??
               FindAction(actions, ActionKind.PlayTactic) ??
               FindAction(actions, ActionKind.EndTurn);
    }

    private static LegalAction? FindAction(
        IEnumerable<LegalAction> actions,
        ActionKind kind) =>
        actions.FirstOrDefault(action => action.Command.Action == kind);

    private static LegalAction? FindLeaderAttack(IEnumerable<LegalAction> actions) =>
        actions.FirstOrDefault(action =>
            action.Command.Action == ActionKind.Attack &&
            action.Command.Target?.Kind == TargetKind.Leader);

    private static LegalAction? FindUnitAttack(IEnumerable<LegalAction> actions) =>
        actions.FirstOrDefault(action =>
            action.Command.Action == ActionKind.Attack &&
            action.Command.Target?.Kind == TargetKind.Unit);

    private static PlayerId Opponent(PlayerId player) => player switch
    {
        PlayerId.Player0 => PlayerId.Player1,
        PlayerId.Player1 => PlayerId.Player0,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unsupported player value."),
    };
}

internal sealed record Gate3CSurrenderOutcome(
    MatchView FinalView,
    int Steps,
    int Covers,
    int Reveals,
    int PrematureViewerCalls,
    int DisposedSessions,
    int ResolvingPublicFrames,
    int ResolvingPrivateLeaks);

internal sealed class Gate3CSurrenderSmoke
{
    private const int MaximumFramesPerTransition = 600;
    private readonly MatchScreen match;
    private readonly Func<Task> nextFrame;
    private int mulligans;

    internal Gate3CSurrenderSmoke(MatchScreen match, Func<Task> nextFrame)
    {
        this.match = match ?? throw new ArgumentNullException(nameof(match));
        this.nextFrame = nextFrame ?? throw new ArgumentNullException(nameof(nextFrame));
    }

    internal async Task<Gate3CSurrenderOutcome> RunAsync()
    {
        for (int guard = 0; guard < 80; guard++)
        {
            HotseatUiState state = match.CiState;
            switch (state.Mode)
            {
                case HotseatUiMode.Covered:
                    if (!state.AwaitingPlayer.HasValue)
                    {
                        throw new InvalidOperationException("A covered state lacks its next viewer.");
                    }
                    match.RevealForCiSmoke();
                    await WaitUntilAsync(
                        () => match.CiState.Mode != HotseatUiMode.Covered && match.HasPresentedSnapshot,
                        "the surrender phase viewer was not revealed");
                    break;
                case HotseatUiMode.Resolving:
                    await WaitUntilAsync(
                        () => match.CiState.Mode != HotseatUiMode.Resolving,
                        "the surrender phase resolving projection did not advance");
                    break;
                case HotseatUiMode.MulliganSelecting:
                    await SubmitMulliganAsync();
                    mulligans++;
                    break;
                case HotseatUiMode.MulliganReview:
                    match.CompleteMulliganReviewThroughSignalForCi();
                    await nextFrame();
                    break;
                case HotseatUiMode.Action:
                    if (mulligans < 2)
                    {
                        throw new InvalidOperationException(
                            "The surrender phase reached actions before both mulligans completed.");
                    }
                    await SubmitSurrenderAsync();
                    break;
                case HotseatUiMode.Finished:
                    return await CompleteAsync(state);
                case HotseatUiMode.Faulted:
                    throw new InvalidOperationException(
                        $"The surrender phase faulted: {state.FailureText}");
                case HotseatUiMode.Reaction:
                    throw new InvalidOperationException("The surrender phase unexpectedly entered a reaction.");
                case HotseatUiMode.Disposed:
                    throw new InvalidOperationException("The surrender phase disposed before its terminal result.");
                default:
                    throw new InvalidOperationException($"Unsupported surrender phase state {state.Mode}.");
            }
        }

        throw new TimeoutException("The surrender phase exceeded its state-transition guard.");
    }

    private async Task SubmitMulliganAsync()
    {
        int before = match.CiSubmissionCount;
        match.ConfirmMulliganThroughSignalForCi();
        ValidateResolving(ActionKind.Mulligan);
        await nextFrame();
        if (match.CiSubmissionCount != before)
        {
            throw new InvalidOperationException("Mulligan submitted before one complete public frame.");
        }
        await WaitUntilAsync(
            () => match.CiSubmissionCount == before + 1,
            "the signal-driven mulligan was not submitted");
        await nextFrame();
    }

    private async Task SubmitSurrenderAsync()
    {
        int before = match.CiSubmissionCount;
        match.SubmitSurrenderThroughSignalsForCi();
        ValidateResolving(ActionKind.Surrender);
        await nextFrame();
        if (match.CiSubmissionCount != before)
        {
            throw new InvalidOperationException("Surrender submitted before one complete public frame.");
        }
        await WaitUntilAsync(
            () => match.CiSubmissionCount == before + 1,
            "the signal-driven surrender was not submitted");
    }

    private void ValidateResolving(ActionKind action)
    {
        if (match.CiState.Mode != HotseatUiMode.Resolving ||
            match.CiState.PublicBoard is null || match.IsPrivacyCoverVisible ||
            match.CiResolvingPrivateLeakCount != 0)
        {
            throw new InvalidOperationException(
                $"{action} did not enter a leak-free public resolving projection.");
        }
    }

    private async Task<Gate3CSurrenderOutcome> CompleteAsync(HotseatUiState state)
    {
        await WaitUntilAsync(
            () => match.HasPresentedSnapshot && match.CiResultVisible,
            "the surrender terminal overlay was not shown");
        MatchView finalView = match.CiState.Snapshot ??
            throw new InvalidOperationException("The surrender terminal snapshot is unavailable.");
        if (finalView.Result == GameResult.Ongoing ||
            !match.CiSuccessfulActionKinds.Contains(ActionKind.Surrender) ||
            mulligans != 2 || match.CiRevealRequestCount < 2 ||
            match.CiMinimumResolvingFrames < 2 ||
            match.CiResolvingPrivateLeakCount != 0 ||
            match.CiPrematureViewerCallCount != 0)
        {
            throw new InvalidOperationException(
                "The restart/surrender phase did not satisfy its signal, privacy, or terminal contract.");
        }

        int steps = match.CiSuccessfulSubmissionCount;
        int covers = match.CiCoverPresentationCount;
        int reveals = match.CiRevealRequestCount;
        int premature = match.CiPrematureViewerCallCount;
        int frames = match.CiMinimumResolvingFrames;
        int leaks = match.CiResolvingPrivateLeakCount;
        match.DisposeForCiSmoke();
        if (match.CiDisposedSessionCount < 1)
        {
            throw new InvalidOperationException("The surrender match session was not disposed.");
        }

        return new Gate3CSurrenderOutcome(
            finalView,
            steps,
            covers,
            reveals,
            premature,
            match.CiDisposedSessionCount,
            frames,
            leaks);
    }

    private async Task WaitUntilAsync(Func<bool> predicate, string failure)
    {
        for (int frame = 0; frame < MaximumFramesPerTransition; frame++)
        {
            if (predicate())
            {
                return;
            }
            if (match.CiState.Mode == HotseatUiMode.Faulted)
            {
                throw new InvalidOperationException(
                    $"{failure}; UI faulted: {match.CiState.FailureText}");
            }
            await nextFrame();
        }
        throw new TimeoutException(
            $"Timed out after {MaximumFramesPerTransition} frames waiting for {failure}.");
    }
}
