// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.Match;
using Scgs.Hotseat;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scgs.GodotClient.Ci;

/// <summary>
/// Drives the real deterministic hot-seat session only as far as the first
/// Player0 Action state and records the unapproved R3 presentation candidate.
/// It observes MatchScreen state and canonical LegalActions; it never creates
/// replacement DTOs or queries a second viewer directly.
/// </summary>
internal sealed class GateR3VisualSlice
{
    private const uint ExpectedSeed = 0xC0DE_C0DEU;
    private const ulong ExpectedFinalRevision = 2;
    private const string PrivateSentinelToken = "SCGS_CI_PRIVATE_SENTINEL_9D2D7B15";
    private const string ProductAssetManifestPath =
        "res://assets/visual/ASSET_MANIFEST.json";
    private const string CandidateAssetManifestPath =
        "res://assets/visual/arena/R3_ASSET_MANIFEST.json";
    private const string LauncherRelativePath = "scripts/ci/PLAY_R3_VISUAL_SLICE.cmd";
    // Imported PNG/GLB source bytes are intentionally replaced by ctex/scn in
    // exported PCKs. Keep their audited source identities in the compiled
    // evidence contract; the external validator recomputes every value from
    // the checkout rather than trusting these constants alone.
    private const string ExpectedCandidateFloorSha256 =
        "9892b03ff0ab3dbe6fb0e733b32461a36e2bc960f7105110f0d6a34b79dd1343";
    private const string ExpectedCandidateGlbSha256 =
        "4ce416e3828dbcdbdf94b407c7f800144497af5afb5f2801bd08b35b267c9108";
    private const string ExpectedCandidateShaderSha256 =
        "1867570d98c986393704b739d5e618d48002aabc0dffdc8528c5e3f679060d06";
    private const double MaximumFramePairMae = 0.01;
    private const int MaximumStableFramePairAttempts = 30;
    private const int MaximumFramesPerTransition = 600;

    private static readonly string[] ExpectedStates =
    [
        "action-idle",
        "hand-hover",
        "source-selected",
    ];

    private readonly Node root;
    private readonly MatchScreen match;
    private readonly IScgsGameSession session;
    private readonly string outputDirectory;
    private readonly Func<Task> nextFrame;
    private readonly DisplayServer.VSyncMode previousVsyncMode;
    private readonly List<int> viewerRequestOrder = [];
    private readonly List<GateR3Capture> captures = [];
    private bool privacySentinelDetectorVerified;
    private bool injectedSentinelArmed;
    private ulong injectedSourceRevision;
    private ulong injectedResultRevision;
    private int resolvingSnapshotRequestsBefore;
    private int resolvingViewerReadsBefore;
    private GateR3TransitionFrame? resolvingPrivacyFrame;
    private GateR3TransitionFrame? coveredPrivacyFrame;
    private GateR3PrivacyScrub? resolvingPrivacyScrub;
    private GateR3PrivacyScrub? privacyScrub;

    internal GateR3VisualSlice(
        Node root,
        MatchScreen match,
        IScgsGameSession session,
        string outputDirectory,
        Func<Task> nextFrame)
    {
        this.root = root ?? throw new ArgumentNullException(nameof(root));
        this.match = match ?? throw new ArgumentNullException(nameof(match));
        this.session = session ?? throw new ArgumentNullException(nameof(session));
        this.nextFrame = nextFrame ?? throw new ArgumentNullException(nameof(nextFrame));
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException(
                "The R3 visual-slice output directory must be absolute.",
                nameof(outputDirectory));
        }
        if (string.Equals(
                DisplayServer.GetName(),
                "headless",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "--r3-visual-slice requires a display-backed renderer.");
        }

        this.outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(this.outputDirectory);
        previousVsyncMode = DisplayServer.WindowGetVsyncMode();
        if (ResolveRequestedViewport(OS.GetCmdlineUserArgs()) is { } requestedViewport)
        {
            // Hosted Windows runners expose a small desktop even though they can
            // render a larger borderless Compatibility window through ANGLE/WARP.
            // Match the established Gate 4B visual-suite setup instead of trusting
            // the engine-level --resolution hint, which the window manager may clamp.
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetPosition(Vector2I.Zero);
            DisplayServer.WindowSetSize(requestedViewport);
        }
        VerifyPrivacySentinelDetector();
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
    }

    private static Vector2I? ResolveRequestedViewport(IReadOnlyList<string> arguments)
    {
        const string prefix = "--ci-visual-viewport=";
        string[] values = arguments
            .Where(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
            .Select(argument => argument[prefix.Length..])
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }
        if (values.Length != 1)
        {
            throw new InvalidOperationException(
                "--ci-visual-viewport may be specified only once.");
        }

        string[] dimensions = values[0].Split('x', StringSplitOptions.TrimEntries);
        if (dimensions.Length != 2 ||
            !int.TryParse(dimensions[0], out int width) ||
            !int.TryParse(dimensions[1], out int height) ||
            (width, height) != (1600, 900))
        {
            throw new InvalidOperationException(
                "The R3 visual slice requires --ci-visual-viewport=1600x900.");
        }
        return new Vector2I(width, height);
    }

    internal async Task<string> RunAsync()
    {
        try
        {
            return await RunCoreAsync();
        }
        finally
        {
            DisplayServer.WindowSetVsyncMode(previousVsyncMode);
        }
    }

    private async Task<string> RunCoreAsync()
    {
        await VerifyOpaqueInitialCoverAsync();
        await DriveToPlayer0ActionAsync();

        HotseatUiState actionState = match.CiState;
        ValidateVisibleSnapshot(actionState);
        MatchView finalView = actionState.Snapshot ??
            throw new InvalidOperationException("The R3 Action state has no viewer snapshot.");
        if (actionState.Mode != HotseatUiMode.Action ||
            finalView.Viewer != PlayerId.Player0 ||
            actionState.Interaction.Step != HotseatSelectionStep.None ||
            actionState.LegalActions.Count == 0)
        {
            throw new InvalidOperationException(
                "The R3 visual slice did not reach an idle Player0 Action state with LegalActions.");
        }
        if (!viewerRequestOrder.SequenceEqual([0, 1, 0]))
        {
            throw new InvalidOperationException(
                "The deterministic R3 reveal order must be Player0, Player1, Player0.");
        }

        Battlefield3DPresenter battlefield = match.FindChild(
            "Battlefield3D",
            recursive: true,
            owned: false) as Battlefield3DPresenter ??
            throw new InvalidOperationException("The R3 visual slice could not find Battlefield3D.");
        if (battlefield.CiArenaProfile != "r3-candidate")
        {
            throw new InvalidOperationException("The R3 candidate arena profile is not active.");
        }
        if (battlefield.CiNearHandPoses.Count == 0 ||
            battlefield.CiHiddenHandCardCount == 0 ||
            !battlefield.CiHiddenHandUsesSharedBack)
        {
            throw new InvalidOperationException(
                "The real Action snapshot does not show a private hand and shared-back hidden hand.");
        }

        await WaitForNearHandPoseSettledAsync(
            battlefield,
            expectedHoveredCount: 0,
            expectedSelectedCount: 0,
            "the action-idle near hand to reach its real target pose");
        captures.Add(await CaptureAsync("action-idle", finalView));

        int hoveredIndex = battlefield.CiNearHandPoses.Count / 2;
        if (!match.SetNearHandHoverForR3(hoveredIndex, hovered: true))
        {
            throw new InvalidOperationException(
                "The real near hand did not enter its hover pose and surface-driven card detail.");
        }
        await WaitForNearHandPoseSettledAsync(
            battlefield,
            expectedHoveredCount: 1,
            expectedSelectedCount: 0,
            "the hovered near hand to finish its real presentation tween");
        captures.Add(await CaptureAsync("hand-hover", finalView));
        if (!match.SetNearHandHoverForR3(hoveredIndex, hovered: false))
        {
            throw new InvalidOperationException("The real near hand did not leave its hover pose.");
        }
        await nextFrame();

        HashSet<ulong> visibleOwnCards = finalView.Players[(int)finalView.Viewer].Hand
            .Where(card => card.InstanceId.HasValue)
            .Select(card => card.InstanceId!.Value)
            .ToHashSet();
        LegalAction selectedAction = actionState.LegalActions
            .Where(action => action.Command.Source != 0 &&
                             visibleOwnCards.Contains(action.Command.Source))
            .OrderByDescending(action =>
                action.Command.Slot.HasValue || action.Command.Target is not null)
            .ThenBy(action => (uint)action.Command.Action)
            .ThenBy(action => action.Command.Source)
            .FirstOrDefault() ??
            throw new InvalidOperationException(
                "The first real Player0 Action state has no visible canonical source.");
        if (!match.SelectLegalSourceForR3(selectedAction))
        {
            throw new InvalidOperationException(
                "The real 3D source gesture did not select its canonical LegalAction source.");
        }
        HotseatUiState selectedState = match.CiState;
        if (selectedState.Mode != HotseatUiMode.Action ||
            selectedState.Interaction.Source != selectedAction.Command.Source ||
            selectedState.Interaction.Step == HotseatSelectionStep.None ||
            selectedState.Snapshot?.Revision != finalView.Revision)
        {
            throw new InvalidOperationException(
                "The R3 source-selected state changed revision or failed to retain the source.");
        }
        await WaitForNearHandPoseSettledAsync(
            battlefield,
            expectedHoveredCount: 0,
            expectedSelectedCount: 1,
            "the selected near hand source to finish its real presentation tween");
        captures.Add(await CaptureAsync("source-selected", finalView));

        if (!captures.Select(capture => capture.State).SequenceEqual(ExpectedStates) ||
            captures.Select(capture => capture.Sha256).Distinct(StringComparer.Ordinal).Count() !=
            ExpectedStates.Length)
        {
            throw new InvalidOperationException(
                "The three R3 candidate states must produce three distinct screenshots.");
        }
        if (captures.Any(capture => !capture.PrivacySentinelAbsent))
        {
            throw new InvalidOperationException(
                "An R3 screenshot contains the GPU privacy sentinel (#ff00ff).");
        }

        GateR3TransitionFrame resolvingFrame = resolvingPrivacyFrame ??
            throw new InvalidOperationException(
                "The injected sentinel did not produce a complete Resolving evidence frame.");
        GateR3TransitionFrame coveredFrame = coveredPrivacyFrame ??
            throw new InvalidOperationException(
                "The injected sentinel did not produce a subsequent Covered evidence frame.");
        GateR3PrivacyScrub scrub = privacyScrub ??
            throw new InvalidOperationException(
                "The injected sentinel did not produce runtime scrub evidence.");
        GateR3BuildProvenance provenance = BuildProvenance();

        var report = new GateR3VisualSliceReport
        {
            ArenaProfile = battlefield.CiArenaProfile,
            ApprovalStatus = "pending_user_approval",
            SessionSetup = new GateR3SessionSetup
            {
                Seed = finalView.RandomSeed,
                FirstPlayer = checked((int)(uint)finalView.FirstPlayer),
                ShuffleDecks = false,
            },
            FinalRevision = finalView.Revision,
            Provenance = provenance,
            CaptureContract = new GateR3CaptureContract(),
            SessionEvidence = new GateR3SessionEvidence
            {
                SessionInterface = nameof(IScgsGameSession),
                SessionRuntimeType = session.GetType().FullName ?? session.GetType().Name,
                StateSource = "HotseatUiState",
                LegalActionsSource = "HotseatUiState.LegalActions",
                SuccessfulMulliganSubmissions = match.CiSuccessfulSubmissionCount,
                FinalLegalActionCount = actionState.LegalActions.Count,
                SelectedActionKind = checked((int)(uint)selectedAction.Command.Action),
                SelectedSource = selectedAction.Command.Source,
            },
            PrivacyEvidence = new GateR3PrivacyEvidence
            {
                OpaqueCoverBeforeFirstView = true,
                ViewerRequestOrder = viewerRequestOrder.ToArray(),
                ExplicitRevealCount = match.CiRevealRequestCount,
                SnapshotRequestCount = match.SnapshotRequestCount,
                ViewerReadRequestCount = match.ViewerReadRequestCount,
                PrematureViewCalls = match.CiPrematureViewerCallCount,
                GpuSentinelDetectorSelfTestPassed = privacySentinelDetectorVerified,
                InjectedSentinelExercised = injectedSentinelArmed,
                InjectedSentinelRuntimeScrubVerified = match.CiPrivacySentinelVerified,
                CandidateCapturesSentinelAbsent = captures.All(capture =>
                    capture.PrivacySentinelAbsent),
                HiddenCardSharedBack = battlefield.CiHiddenHandUsesSharedBack,
                HiddenCardCount = battlefield.CiHiddenHandCardCount,
                InjectedTransition = new GateR3InjectedTransition
                {
                    SourceActionKind = checked((int)(uint)ActionKind.Mulligan),
                    SourceViewer = checked((int)(uint)PlayerId.Player0),
                    SourceRevision = injectedSourceRevision,
                    ResultRevision = injectedResultRevision,
                    Resolving = resolvingFrame,
                    Covered = coveredFrame,
                },
                Scrub = scrub,
            },
            Viewport = new GateR3Viewport
            {
                Width = captures[0].Width,
                Height = captures[0].Height,
            },
            Captures = captures.ToArray(),
        };
        ValidateRuntimeReport(report);
        string reportPath = Path.Combine(outputDirectory, "r3-visual-slice.json");
        WriteReportAtomically(reportPath, report);
        return reportPath;
    }

    private async Task VerifyOpaqueInitialCoverAsync()
    {
        if (match.CiState.Mode != HotseatUiMode.Covered ||
            match.CiState.AwaitingPlayer != PlayerId.Player0 ||
            !match.IsPrivacyCoverVisible || match.HasPresentedSnapshot ||
            match.SnapshotRequestCount != 0 || match.ViewerReadRequestCount != 0 ||
            match.CiPrematureViewerCallCount != 0)
        {
            throw new InvalidOperationException(
                "The R3 session did not begin behind the opaque Player0 privacy cover.");
        }

        for (int frame = 0; frame < 2; frame++)
        {
            using Image ignored = await ReadCompletedFrameAsync();
            if (!match.IsPrivacyCoverVisible || match.HasPresentedSnapshot ||
                match.SnapshotRequestCount != 0 || match.ViewerReadRequestCount != 0)
            {
                throw new InvalidOperationException(
                    "The R3 initial cover was revealed before the explicit viewer request.");
            }
        }
    }

    private async Task DriveToPlayer0ActionAsync()
    {
        while (true)
        {
            HotseatUiState state = match.CiState;
            switch (state.Mode)
            {
                case HotseatUiMode.Covered:
                    if (injectedSentinelArmed && coveredPrivacyFrame is null)
                    {
                        await CaptureCoveredPrivacyEvidenceAsync(state);
                    }
                    await RevealCoveredViewerAsync(state);
                    break;
                case HotseatUiMode.Resolving:
                    await WaitUntilAsync(
                        () => match.CiState.Mode != HotseatUiMode.Resolving &&
                              (match.CiState.Mode == HotseatUiMode.Covered ||
                               match.HasPresentedSnapshot),
                        "the real mulligan submission was not rendered after Resolving");
                    break;
                case HotseatUiMode.MulliganSelecting:
                    ValidateVisibleSnapshot(state);
                    await WaitForRenderedEventsAsync();
                    state = match.CiState;
                    if (state.SelectedAction?.Command is not { } mulligan ||
                        mulligan.Action != ActionKind.Mulligan ||
                        mulligan.MulliganCards.Count != 0 ||
                        !state.LegalActions.Contains(state.SelectedAction))
                    {
                        throw new InvalidOperationException(
                            "The real LegalActions did not provide the canonical empty mulligan.");
                    }
                    bool injectSentinel = !injectedSentinelArmed &&
                                          state.Snapshot?.Viewer == PlayerId.Player0;
                    if (injectSentinel)
                    {
                        injectedSourceRevision = state.Snapshot!.Revision;
                        if (injectedSourceRevision != 0)
                        {
                            throw new InvalidOperationException(
                                "The injected sentinel must surround the first revision-0 mulligan.");
                        }
                        resolvingSnapshotRequestsBefore = match.SnapshotRequestCount;
                        resolvingViewerReadsBefore = match.ViewerReadRequestCount;
                        match.ArmResolvingPrivacySentinelForCi(
                            Path.Combine(outputDirectory, "privacy-resolving.png"));
                        injectedSentinelArmed = true;
                    }
                    match.ConfirmCurrentSelectionForCi();
                    await WaitUntilAsync(
                        () => match.CiState.Mode == HotseatUiMode.Resolving,
                        "the real empty mulligan did not enter Resolving");
                    if (injectSentinel)
                    {
                        await CaptureResolvingPrivacyEvidenceAsync();
                    }
                    break;
                case HotseatUiMode.MulliganReview:
                    await WaitUntilAsync(
                        () => match.HasPresentedSnapshot && !match.IsPrivacyCoverVisible,
                        "the real mulligan review was not visibly presented");
                    state = match.CiState;
                    ValidateVisibleSnapshot(state);
                    if (injectedSentinelArmed && injectedResultRevision == 0)
                    {
                        injectedResultRevision = state.Snapshot!.Revision;
                        if (injectedResultRevision != 1)
                        {
                            throw new InvalidOperationException(
                                "The sentinel-wrapped first mulligan must produce revision 1.");
                        }
                    }
                    await WaitForRenderedEventsAsync();
                    match.CompleteMulliganReviewForCi();
                    await nextFrame();
                    break;
                case HotseatUiMode.Action:
                    ValidateVisibleSnapshot(state);
                    await WaitForRenderedEventsAsync();
                    state = match.CiState;
                    if (state.Snapshot?.Viewer == PlayerId.Player0)
                    {
                        if (match.CiSuccessfulSubmissionCount != 2)
                        {
                            throw new InvalidOperationException(
                                "The R3 Action state was not reached through two real mulligan submissions.");
                        }
                        return;
                    }
                    throw new InvalidOperationException(
                        "The deterministic first Action viewer is not Player0.");
                case HotseatUiMode.Faulted:
                    throw new InvalidOperationException(
                        $"The hot-seat UI faulted: {state.FailureText}");
                default:
                    throw new InvalidOperationException(
                        $"Unexpected R3 hot-seat mode before Action: {state.Mode}.");
            }
        }
    }

    private async Task CaptureResolvingPrivacyEvidenceAsync()
    {
        string screenshotPath = Path.Combine(outputDirectory, "privacy-resolving.png");
        await WaitUntilAsync(
            () => match.CiResolvingScreenshotCaptured && File.Exists(screenshotPath),
            "the injected private sentinel did not produce its complete Resolving frame");
        if (match.CiState.Mode != HotseatUiMode.Resolving ||
            match.SnapshotRequestCount != resolvingSnapshotRequestsBefore ||
            match.ViewerReadRequestCount != resolvingViewerReadsBefore)
        {
            throw new InvalidOperationException(
                "Resolving performed a viewer-scoped read or completed before its injected-sentinel audit.");
        }

        resolvingPrivacyFrame = DescribeTransitionFrame(
            "Resolving",
            screenshotPath,
            injectedSourceRevision,
            resolvingSnapshotRequestsBefore,
            match.SnapshotRequestCount,
            resolvingViewerReadsBefore,
            match.ViewerReadRequestCount);
        resolvingPrivacyScrub = ObservePrivacyScrub();
        RequireCompletePrivacyScrub(resolvingPrivacyScrub, "Resolving");
    }

    private async Task CaptureCoveredPrivacyEvidenceAsync(HotseatUiState covered)
    {
        if (covered.Mode != HotseatUiMode.Covered ||
            covered.AwaitingPlayer != PlayerId.Player1 ||
            !match.IsPrivacyCoverVisible || match.HasPresentedSnapshot ||
            injectedResultRevision != 1 || resolvingPrivacyFrame is null ||
            resolvingPrivacyScrub is null)
        {
            throw new InvalidOperationException(
                "The injected sentinel did not transition through revision 1 to the opaque Player1 cover.");
        }

        int requestsBefore = match.SnapshotRequestCount;
        int viewerReadsBefore = match.ViewerReadRequestCount;
        using Image image = await ReadCompletedFrameAsync();
        if (match.CiState.Mode != HotseatUiMode.Covered ||
            !match.IsPrivacyCoverVisible || match.HasPresentedSnapshot ||
            match.SnapshotRequestCount != requestsBefore ||
            match.ViewerReadRequestCount != viewerReadsBefore)
        {
            throw new InvalidOperationException(
                "The subsequent Covered frame performed a viewer-scoped read or exposed private presentation.");
        }

        string screenshotPath = Path.Combine(outputDirectory, "privacy-covered.png");
        SaveImageAtomically(image, screenshotPath, "privacy-covered");
        coveredPrivacyFrame = DescribeTransitionFrame(
            "Covered",
            screenshotPath,
            injectedResultRevision,
            requestsBefore,
            match.SnapshotRequestCount,
            viewerReadsBefore,
            match.ViewerReadRequestCount);

        GateR3PrivacyScrub coveredScrub = ObservePrivacyScrub();
        RequireCompletePrivacyScrub(coveredScrub, "Covered");
        privacyScrub = new GateR3PrivacyScrub
        {
            PrivateTextCleared = resolvingPrivacyScrub.PrivateTextCleared &&
                                 coveredScrub.PrivateTextCleared,
            PrivateMetadataCleared = resolvingPrivacyScrub.PrivateMetadataCleared &&
                                     coveredScrub.PrivateMetadataCleared,
            PrivateMaterialCleared = resolvingPrivacyScrub.PrivateMaterialCleared &&
                                     coveredScrub.PrivateMaterialCleared,
            CollisionsDisabled = resolvingPrivacyScrub.CollisionsDisabled &&
                                 coveredScrub.CollisionsDisabled,
            DragTokensCleared = resolvingPrivacyScrub.DragTokensCleared &&
                                coveredScrub.DragTokensCleared,
            TweensCancelled = resolvingPrivacyScrub.TweensCancelled &&
                              coveredScrub.TweensCancelled,
            CallbacksCleared = resolvingPrivacyScrub.CallbacksCleared &&
                               coveredScrub.CallbacksCleared,
            ResolvingPrivateLeakCount = match.CiResolvingPrivateLeakCount,
            SpatialPrivateLeakCount = match.CiSpatialPrivateLeaks,
            ForbiddenSentinelTokenCount = 0,
        };
        RequireCompletePrivacyScrub(privacyScrub, "Resolving-to-Covered");
    }

    private GateR3PrivacyScrub ObservePrivacyScrub()
    {
        Battlefield3DPresenter battlefield = FindBattlefield();
        int forbiddenTokens = battlefield.CiCountForbiddenToken(PrivateSentinelToken);
        bool sentinelRuntimeScrubbed = match.CiPrivacySentinelVerified &&
                                       forbiddenTokens == 0;
        return new GateR3PrivacyScrub
        {
            // MatchScreen's sentinel hook injects the token into labels,
            // tooltips, metadata, callback keys and definition-specific GPU
            // material state. Its resolving audit plus the presenter token
            // scan provide independent scene-tree and actor-pool evidence.
            PrivateTextCleared = sentinelRuntimeScrubbed,
            PrivateMetadataCleared = sentinelRuntimeScrubbed,
            PrivateMaterialCleared = sentinelRuntimeScrubbed,
            CollisionsDisabled = battlefield.CiCollisionEnabledCount == 0,
            DragTokensCleared = !battlefield.CiHasActiveDrag &&
                                battlefield.CiStableSurfaceLookupCount == 0 &&
                                !match.CiHasTransientDragData,
            TweensCancelled = CardActorTweensAreCleared(battlefield),
            CallbacksCleared = match.CiResolvingPrivateLeakCount == 0,
            ResolvingPrivateLeakCount = match.CiResolvingPrivateLeakCount,
            SpatialPrivateLeakCount = match.CiSpatialPrivateLeaks,
            ForbiddenSentinelTokenCount = forbiddenTokens,
        };
    }

    private static bool CardActorTweensAreCleared(Battlefield3DPresenter battlefield)
    {
        System.Reflection.FieldInfo tweenField = typeof(CardActor3D).GetField(
            "_hoverTween",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                "CardActor3D no longer exposes the audited private hover tween field.");
        return EnumerateSubtree(battlefield)
            .OfType<CardActor3D>()
            .All(actor => tweenField.GetValue(actor) is null);
    }

    private static IEnumerable<Node> EnumerateSubtree(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren())
        {
            foreach (Node descendant in EnumerateSubtree(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RequireCompletePrivacyScrub(GateR3PrivacyScrub scrub, string state)
    {
        if (!scrub.PrivateTextCleared || !scrub.PrivateMetadataCleared ||
            !scrub.PrivateMaterialCleared || !scrub.CollisionsDisabled ||
            !scrub.DragTokensCleared || !scrub.TweensCancelled ||
            !scrub.CallbacksCleared || scrub.ResolvingPrivateLeakCount != 0 ||
            scrub.SpatialPrivateLeakCount != 0 ||
            scrub.ForbiddenSentinelTokenCount != 0)
        {
            throw new InvalidOperationException(
                $"The {state} private sentinel scrub was incomplete: {scrub}.");
        }
    }

    private Battlefield3DPresenter FindBattlefield() => match.FindChild(
        "Battlefield3D",
        recursive: true,
        owned: false) as Battlefield3DPresenter ??
        throw new InvalidOperationException("The R3 visual slice could not find Battlefield3D.");

    private async Task RevealCoveredViewerAsync(HotseatUiState covered)
    {
        PlayerId viewer = covered.AwaitingPlayer ??
            throw new InvalidOperationException("The R3 cover does not name its awaiting viewer.");
        if (!match.IsPrivacyCoverVisible || match.HasPresentedSnapshot)
        {
            throw new InvalidOperationException(
                "The R3 handoff did not clear private presentation behind its opaque cover.");
        }

        // The initial cover already survived two FramePostDraws. Make every
        // later device handoff equally observable before CI asks to reveal the
        // next real viewer.
        if (viewerRequestOrder.Count > 0)
        {
            using Image ignored = await ReadCompletedFrameAsync();
            if (!match.IsPrivacyCoverVisible || match.HasPresentedSnapshot ||
                match.CiState.Mode != HotseatUiMode.Covered)
            {
                throw new InvalidOperationException(
                    "The R3 handoff cover did not survive a complete drawn frame.");
            }
        }

        int revealsBefore = match.CiRevealRequestCount;
        int snapshotsBefore = match.SnapshotRequestCount;
        viewerRequestOrder.Add(checked((int)(uint)viewer));
        match.RevealForCiSmoke();
        await WaitUntilAsync(
            () => match.CiState.Mode != HotseatUiMode.Covered &&
                  match.HasPresentedSnapshot &&
                  !match.IsPrivacyCoverVisible,
            "the explicitly requested R3 viewer was not revealed");
        if (match.CiRevealRequestCount != revealsBefore + 1 ||
            match.SnapshotRequestCount <= snapshotsBefore)
        {
            throw new InvalidOperationException(
                "The explicit R3 reveal did not produce its real viewer snapshot.");
        }
        ValidateVisibleSnapshot(match.CiState);
    }

    private void ValidateVisibleSnapshot(HotseatUiState state)
    {
        MatchView view = state.Snapshot ??
            throw new InvalidOperationException("A visible R3 state has no snapshot.");
        if (state.IsCovered || !state.Viewer.HasValue ||
            !match.HasPresentedSnapshot || match.IsPrivacyCoverVisible ||
            view.Viewer != state.Viewer.Value || view.Players.Length != 2 ||
            view.RandomSeed != ExpectedSeed || view.FirstPlayer != PlayerId.Player0)
        {
            throw new InvalidOperationException(
                "The real R3 viewer snapshot violates deterministic or privacy metadata: " +
                $"mode={state.Mode}, state_viewer={state.Viewer}, view_viewer={view.Viewer}, " +
                $"presented={match.HasPresentedSnapshot}, cover={match.IsPrivacyCoverVisible}, " +
                $"players={view.Players.Length}, seed={view.RandomSeed}, first={view.FirstPlayer}.");
        }
        PlayerView own = view.Players[(int)view.Viewer];
        PlayerId opponent = view.Viewer == PlayerId.Player0
            ? PlayerId.Player1
            : PlayerId.Player0;
        PlayerView hidden = view.Players[(int)opponent];
        if ((ulong)own.Hand.Length != own.HandCount || hidden.Hand.Length != 0 ||
            (ulong)match.OpponentHandBackCount != hidden.HandCount ||
            !match.RenderedLabelsMatch(view))
        {
            throw new InvalidOperationException(
                "The R3 rendered hand or labels disagree with the viewer-scoped DTO.");
        }
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
            "the R3 event batch was not visibly acknowledged");
        if (match.CiEventAcknowledgeCount <= acknowledgements)
        {
            throw new InvalidOperationException(
                "The R3 event cursor advanced without a rendered acknowledgement.");
        }
        await nextFrame();
    }

    private async Task WaitForSafeFxToSettleAsync()
    {
        Label3D safeFx = match.FindChild(
            "SafeFxLabel",
            recursive: true,
            owned: false) as Label3D ??
            throw new InvalidOperationException("The R3 battlefield SafeFxLabel is unavailable.");
        await WaitUntilAsync(
            () => !safeFx.Visible && string.IsNullOrEmpty(safeFx.Text),
            "the viewer-safe battlefield FX label to become fully hidden");
        // Preserve one complete quiet process frame before the first of each
        // screenshot's two measured FramePostDraw samples.
        await nextFrame();
    }

    private async Task WaitForNearHandPoseSettledAsync(
        Battlefield3DPresenter battlefield,
        int expectedHoveredCount,
        int expectedSelectedCount,
        string failure)
    {
        int consecutiveCompletedDraws = 0;
        for (int frame = 0; frame < MaximumFramesPerTransition; frame++)
        {
            // VSync is intentionally disabled for this collector, so elapsed
            // frame count is not evidence that the 180 ms presentation tween
            // has completed. Observe the actual card transforms after complete
            // draws and require the target pose to persist across two of them.
            using Image ignored = await ReadCompletedFrameAsync();
            if (NearHandActorsMatchTargetPoses(
                    battlefield,
                    expectedHoveredCount,
                    expectedSelectedCount))
            {
                consecutiveCompletedDraws++;
                if (consecutiveCompletedDraws == 2)
                {
                    return;
                }
            }
            else
            {
                consecutiveCompletedDraws = 0;
            }

            if (match.CiState.Mode == HotseatUiMode.Faulted)
            {
                throw new InvalidOperationException(
                    $"{failure}; the UI faulted: {match.CiState.FailureText}");
            }
        }

        throw new TimeoutException(
            $"Timed out after {MaximumFramesPerTransition} completed draws while waiting for {failure}.");
    }

    private static bool NearHandActorsMatchTargetPoses(
        Battlefield3DPresenter battlefield,
        int expectedHoveredCount,
        int expectedSelectedCount)
    {
        HandCardPose[] poses = battlefield.CiNearHandPoses.ToArray();
        if (poses.Length == 0 ||
            poses.Count(pose => pose.Hovered) != expectedHoveredCount ||
            poses.Count(pose => pose.Selected) != expectedSelectedCount)
        {
            return false;
        }

        CardActor3D[] actors = battlefield.GetChildren()
            .OfType<CardActor3D>()
            .Where(actor => actor.Visible &&
                            actor.CiLayout == BattlefieldCardLayout.NearHand &&
                            actor.Surface is { Kind: BattlefieldSurfaceKind.HandCard })
            .ToArray();
        if (actors.Length != poses.Length)
        {
            return false;
        }

        foreach (HandCardPose pose in poses)
        {
            CardActor3D? actor = actors.SingleOrDefault(candidate =>
                candidate.Surface is { } surface &&
                surface.Player == pose.Player &&
                surface.Index == pose.Index);
            if (actor is null || !TransformsApproximatelyEqual(actor.Transform, pose.Transform))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TransformsApproximatelyEqual(Transform3D left, Transform3D right) =>
        VectorsApproximatelyEqual(left.Origin, right.Origin) &&
        VectorsApproximatelyEqual(left.Basis.X, right.Basis.X) &&
        VectorsApproximatelyEqual(left.Basis.Y, right.Basis.Y) &&
        VectorsApproximatelyEqual(left.Basis.Z, right.Basis.Z);

    private static bool VectorsApproximatelyEqual(Vector3 left, Vector3 right)
    {
        const float tolerance = 0.0001f;
        return MathF.Abs(left.X - right.X) <= tolerance &&
               MathF.Abs(left.Y - right.Y) <= tolerance &&
               MathF.Abs(left.Z - right.Z) <= tolerance;
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
                    $"{failure}; the UI faulted: {match.CiState.FailureText}");
            }
            await nextFrame();
        }
        throw new TimeoutException(
            $"Timed out after {MaximumFramesPerTransition} frames while waiting for {failure}.");
    }

    private async Task<GateR3Capture> CaptureAsync(string state, MatchView view)
    {
        await WaitForSafeFxToSettleAsync();
        Image previous = await ReadCompletedFrameAsync();
        try
        {
            for (int attempt = 0; attempt < MaximumStableFramePairAttempts; attempt++)
            {
                Image? current = null;
                try
                {
                    current = await ReadCompletedFrameAsync();
                    if (previous.GetWidth() <= 0 || previous.GetHeight() <= 0 ||
                        current.GetWidth() != previous.GetWidth() ||
                        current.GetHeight() != previous.GetHeight())
                    {
                        throw new InvalidOperationException(
                            $"R3 capture {state} produced an empty or changing viewport.");
                    }
                    double mae = MeasureFramePairMae(previous, current);
                    if (mae <= MaximumFramePairMae)
                    {
                        return SaveCapture(state, view, current, mae);
                    }

                    previous.Dispose();
                    previous = current;
                    current = null;
                }
                finally
                {
                    current?.Dispose();
                }
            }
        }
        finally
        {
            previous.Dispose();
        }

        throw new InvalidOperationException(
            $"R3 capture {state} did not produce two stable consecutive FramePostDraws.");
    }

    private GateR3Capture SaveCapture(
        string state,
        MatchView view,
        Image image,
        double framePairMae)
    {
        bool sentinelAbsent = !ContainsGpuPrivacySentinel(image);
        string filename = $"{state}.png";
        string path = Path.Combine(outputDirectory, filename);
        string temporaryPath = Path.Combine(
            outputDirectory,
            $".{state}.tmp-{System.Environment.ProcessId}.png");
        try
        {
            Error result = image.SavePng(temporaryPath);
            if (result != Error.Ok)
            {
                throw new IOException(
                    $"Godot could not save R3 capture {state} ({result}).");
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return new GateR3Capture
        {
            State = state,
            Viewer = checked((int)(uint)view.Viewer),
            Revision = view.Revision,
            Width = image.GetWidth(),
            Height = image.GetHeight(),
            File = filename,
            Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))
                .ToLowerInvariant(),
            StableFramePostDraws = 2,
            FramePairMae = framePairMae,
            PrivacySentinelAbsent = sentinelAbsent,
        };
    }

    private static GateR3TransitionFrame DescribeTransitionFrame(
        string state,
        string path,
        ulong revision,
        int snapshotRequestsBefore,
        int snapshotRequestsAfter,
        int viewerReadsBefore,
        int viewerReadsAfter)
    {
        using Image image = Image.LoadFromFile(path);
        if (image is null || image.IsEmpty())
        {
            throw new IOException($"The R3 {state} privacy evidence is not a readable PNG: {path}");
        }
        bool sentinelAbsent = !ContainsGpuPrivacySentinel(image);
        if (!sentinelAbsent)
        {
            throw new InvalidOperationException(
                $"The injected GPU privacy sentinel survived the {state} frame.");
        }
        return new GateR3TransitionFrame
        {
            Mode = state,
            Revision = revision,
            Width = image.GetWidth(),
            Height = image.GetHeight(),
            File = Path.GetFileName(path),
            Sha256 = Sha256(File.ReadAllBytes(path)),
            CompleteFramePostDraws = 1,
            SnapshotRequestsBefore = snapshotRequestsBefore,
            SnapshotRequestsAfter = snapshotRequestsAfter,
            ViewerReadsBefore = viewerReadsBefore,
            ViewerReadsAfter = viewerReadsAfter,
            PrivacySentinelAbsent = true,
        };
    }

    private static void SaveImageAtomically(Image image, string path, string state)
    {
        string temporaryPath = Path.Combine(
            Path.GetDirectoryName(path) ?? throw new IOException("Missing screenshot directory."),
            $".{Path.GetFileNameWithoutExtension(path)}.tmp-{System.Environment.ProcessId}.png");
        try
        {
            Error result = image.SavePng(temporaryPath);
            if (result != Error.Ok)
            {
                throw new IOException(
                    $"Godot could not save R3 {state} evidence ({result}).");
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static GateR3BuildProvenance BuildProvenance()
    {
        byte[] productManifest = ReadRequiredResource(ProductAssetManifestPath);
        byte[] candidateManifest = ReadRequiredResource(CandidateAssetManifestPath);
        byte[] launcher = File.ReadAllBytes(ResolveLauncherPath());

        ValidateAssetManifest(productManifest, expectedCount: 34, "approved product");
        ValidateCandidateManifest(candidateManifest, ExpectedCandidateFloorSha256);
        GateR3BuildIdentity build = ResolveBuildIdentity();
        return new GateR3BuildProvenance
        {
            CommitSha = build.CommitSha,
            CommitSource = build.Source,
            WorkingTreeDirty = build.WorkingTreeDirty,
            ProductAssetManifest = new GateR3ManifestIdentity
            {
                ResourcePath = ProductAssetManifestPath,
                Sha256 = Sha256(productManifest),
                AssetCount = 34,
            },
            CandidateAssetManifest = new GateR3ManifestIdentity
            {
                ResourcePath = CandidateAssetManifestPath,
                Sha256 = Sha256(candidateManifest),
                AssetCount = 1,
            },
            CandidateFloorSha256 = ExpectedCandidateFloorSha256,
            CandidateGlbSha256 = ExpectedCandidateGlbSha256,
            CandidateShaderSha256 = ExpectedCandidateShaderSha256,
            LauncherSha256 = Sha256(launcher),
        };
    }

    private static byte[] ReadRequiredResource(string path)
    {
        byte[] bytes = Godot.FileAccess.GetFileAsBytes(path);
        if (bytes.Length == 0)
        {
            throw new IOException($"Required R3 provenance resource is missing or empty: {path}");
        }
        return bytes;
    }

    private static void ValidateAssetManifest(byte[] bytes, int expectedCount, string label)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("assets", out JsonElement assets) ||
            assets.ValueKind != JsonValueKind.Array ||
            assets.GetArrayLength() != expectedCount)
        {
            throw new InvalidOperationException(
                $"The {label} asset manifest must contain exactly {expectedCount} entries.");
        }
    }

    private static void ValidateCandidateManifest(byte[] bytes, string floorSha256)
    {
        ValidateAssetManifest(bytes, expectedCount: 1, "R3 candidate");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        JsonElement entry = root.GetProperty("assets")[0];
        if (root.GetProperty("schema_version").GetInt32() != 1 ||
            root.GetProperty("gate").GetString() != "4B-R3.1" ||
            entry.GetProperty("path").GetString() !=
            "client/godot/assets/visual/arena/r3_industrial_floor_albedo.png" ||
            entry.GetProperty("sha256").GetString() != floorSha256)
        {
            throw new InvalidOperationException(
                "The R3 candidate manifest does not bind its sole floor asset.");
        }
    }

    private static string ResolveLauncherPath()
    {
        string? explicitPath = System.Environment.GetEnvironmentVariable(
            "SCGS_R3_LAUNCHER_PATH");
        if (!string.IsNullOrWhiteSpace(explicitPath) &&
            Path.IsPathFullyQualified(explicitPath) && File.Exists(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        foreach (string directory in RuntimeSearchDirectories())
        {
            string packaged = Path.Combine(directory, "PLAY_R3_VISUAL_SLICE.cmd");
            if (File.Exists(packaged))
            {
                return packaged;
            }
        }
        string? repository = TryFindRepositoryRoot();
        string source = repository is null
            ? string.Empty
            : Path.Combine(repository, LauncherRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!string.IsNullOrEmpty(source) && File.Exists(source))
        {
            return source;
        }
        throw new IOException(
            "The R3 launcher is unavailable beside the export and in the source repository.");
    }

    private static GateR3BuildIdentity ResolveBuildIdentity()
    {
        foreach (string variable in new[] { "SCGS_BUILD_COMMIT", "GITHUB_SHA" })
        {
            string? value = System.Environment.GetEnvironmentVariable(variable);
            if (TryNormalizeCommit(value, out string commit))
            {
                bool dirty = variable == "SCGS_BUILD_COMMIT" &&
                             bool.TryParse(
                                 System.Environment.GetEnvironmentVariable("SCGS_BUILD_DIRTY"),
                                 out bool requestedDirty) && requestedDirty;
                return new GateR3BuildIdentity(commit, variable, dirty);
            }
        }

        string? repository = TryFindRepositoryRoot();
        if (repository is not null &&
            TryRunGit(repository, ["rev-parse", "HEAD"], out string revision) &&
            TryNormalizeCommit(revision, out string gitCommit) &&
            TryRunGit(repository, ["status", "--porcelain"], out string status))
        {
            return new GateR3BuildIdentity(
                gitCommit,
                "git",
                !string.IsNullOrWhiteSpace(status));
        }

        foreach (string path in BuildInfoCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }
            string? commitLine = File.ReadLines(path)
                .FirstOrDefault(line => line.StartsWith("commit=", StringComparison.Ordinal));
            if (TryNormalizeCommit(commitLine?["commit=".Length..], out string buildCommit))
            {
                return new GateR3BuildIdentity(buildCommit, "BUILD_INFO", false);
            }
        }

        throw new InvalidOperationException(
            "R3 evidence requires a 40-character commit SHA from the environment, Git, or BUILD_INFO.txt.");
    }

    private static IEnumerable<string> BuildInfoCandidates()
    {
        foreach (string directory in RuntimeSearchDirectories())
        {
            yield return Path.Combine(directory, "licenses", "BUILD_INFO.txt");
            yield return Path.GetFullPath(Path.Combine(
                directory,
                "..",
                "Resources",
                "licenses",
                "BUILD_INFO.txt"));
        }
    }

    private static IEnumerable<string> RuntimeSearchDirectories()
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }
            string full;
            try
            {
                full = Path.GetFullPath(value);
            }
            catch (Exception)
            {
                return;
            }
            directories.Add(full);
            DirectoryInfo? parent = Directory.GetParent(full);
            if (parent is not null)
            {
                directories.Add(parent.FullName);
            }
        }

        Add(Directory.GetCurrentDirectory());
        Add(AppContext.BaseDirectory);
        string executable = OS.GetExecutablePath();
        if (!string.IsNullOrWhiteSpace(executable))
        {
            Add(Path.GetDirectoryName(executable));
        }
        return directories;
    }

    private static string? TryFindRepositoryRoot()
    {
        IEnumerable<string> starts = new[]
        {
            ProjectSettings.GlobalizePath("res://"),
            Directory.GetCurrentDirectory(),
        };
        foreach (string start in starts)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }
            DirectoryInfo? directory;
            try
            {
                directory = new DirectoryInfo(Path.GetFullPath(start));
            }
            catch (Exception)
            {
                continue;
            }
            for (int depth = 0; directory is not null && depth < 8; depth++, directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, ".git")) &&
                    File.Exists(Path.Combine(
                        directory.FullName,
                        LauncherRelativePath.Replace('/', Path.DirectorySeparatorChar))))
                {
                    return directory.FullName;
                }
            }
        }
        return null;
    }

    private static bool TryRunGit(
        string repository,
        IReadOnlyList<string> arguments,
        out string output)
    {
        output = string.Empty;
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-C");
            start.ArgumentList.Add(repository);
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }
            using System.Diagnostics.Process process =
                System.Diagnostics.Process.Start(start) ??
                throw new InvalidOperationException("Git did not start.");
            output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            output = string.Empty;
            return false;
        }
    }

    private static bool TryNormalizeCommit(string? value, out string commit)
    {
        commit = value?.Trim().ToLowerInvariant() ?? string.Empty;
        return commit.Length == 40 && commit.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private async Task<Image> ReadCompletedFrameAsync()
    {
        await root.ToSignal(root.GetTree(), SceneTree.SignalName.ProcessFrame);
        await root.ToSignal(
            RenderingServer.Singleton,
            RenderingServer.SignalName.FramePostDraw);
        return root.GetViewport().GetTexture().GetImage();
    }

    private static double MeasureFramePairMae(Image first, Image second)
    {
        if (first.GetFormat() != Image.Format.Rgba8)
        {
            first.Convert(Image.Format.Rgba8);
        }
        if (second.GetFormat() != Image.Format.Rgba8)
        {
            second.Convert(Image.Format.Rgba8);
        }
        byte[] left = first.GetData();
        byte[] right = second.GetData();
        if (left.Length != right.Length || left.Length % 4 != 0)
        {
            return double.PositiveInfinity;
        }
        long difference = 0;
        for (int index = 0; index < left.Length; index += 4)
        {
            difference += Math.Abs(left[index] - right[index]);
            difference += Math.Abs(left[index + 1] - right[index + 1]);
            difference += Math.Abs(left[index + 2] - right[index + 2]);
        }
        long channels = checked((long)(left.Length / 4) * 3);
        return difference / (channels * 255.0);
    }

    private void VerifyPrivacySentinelDetector()
    {
        using Image sentinel = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        sentinel.SetPixel(0, 0, new Color(1.0f, 0.0f, 1.0f, 1.0f));
        privacySentinelDetectorVerified = ContainsGpuPrivacySentinel(sentinel);
        if (!privacySentinelDetectorVerified)
        {
            throw new InvalidOperationException(
                "The R3 GPU privacy sentinel detector failed its #ff00ff self-check.");
        }
    }

    private static bool ContainsGpuPrivacySentinel(Image image)
    {
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        byte[] pixels = image.GetData();
        for (int index = 0; index + 3 < pixels.Length; index += 4)
        {
            if (pixels[index] >= 250 && pixels[index + 1] <= 5 &&
                pixels[index + 2] >= 250 && pixels[index + 3] >= 250)
            {
                return true;
            }
        }
        return false;
    }

    private static void ValidateRuntimeReport(GateR3VisualSliceReport report)
    {
        if (report.SessionSetup.Seed != ExpectedSeed ||
            report.SessionSetup.FirstPlayer != 0 || report.SessionSetup.ShuffleDecks ||
            report.FinalRevision != ExpectedFinalRevision ||
            report.SessionEvidence.SuccessfulMulliganSubmissions != 2 ||
            report.PrivacyEvidence.ExplicitRevealCount != 3 ||
            report.PrivacyEvidence.SnapshotRequestCount != 5 ||
            report.PrivacyEvidence.ViewerReadRequestCount <
                report.PrivacyEvidence.SnapshotRequestCount ||
            report.PrivacyEvidence.PrematureViewCalls != 0 ||
            !report.PrivacyEvidence.GpuSentinelDetectorSelfTestPassed ||
            !report.PrivacyEvidence.InjectedSentinelExercised ||
            !report.PrivacyEvidence.InjectedSentinelRuntimeScrubVerified ||
            !report.PrivacyEvidence.CandidateCapturesSentinelAbsent ||
            !report.PrivacyEvidence.HiddenCardSharedBack ||
            report.PrivacyEvidence.InjectedTransition.SourceActionKind !=
                checked((int)(uint)ActionKind.Mulligan) ||
            report.PrivacyEvidence.InjectedTransition.SourceViewer != 0 ||
            report.PrivacyEvidence.InjectedTransition.SourceRevision != 0 ||
            report.PrivacyEvidence.InjectedTransition.ResultRevision != 1 ||
            report.PrivacyEvidence.InjectedTransition.Resolving.Mode != "Resolving" ||
            report.PrivacyEvidence.InjectedTransition.Covered.Mode != "Covered" ||
            report.PrivacyEvidence.InjectedTransition.Resolving.SnapshotRequestsBefore !=
                report.PrivacyEvidence.InjectedTransition.Resolving.SnapshotRequestsAfter ||
            report.PrivacyEvidence.InjectedTransition.Covered.SnapshotRequestsBefore !=
                report.PrivacyEvidence.InjectedTransition.Covered.SnapshotRequestsAfter ||
            report.PrivacyEvidence.InjectedTransition.Resolving.ViewerReadsBefore !=
                report.PrivacyEvidence.InjectedTransition.Resolving.ViewerReadsAfter ||
            report.PrivacyEvidence.InjectedTransition.Covered.ViewerReadsBefore !=
                report.PrivacyEvidence.InjectedTransition.Covered.ViewerReadsAfter ||
            !report.PrivacyEvidence.InjectedTransition.Resolving.PrivacySentinelAbsent ||
            !report.PrivacyEvidence.InjectedTransition.Covered.PrivacySentinelAbsent ||
            !report.PrivacyEvidence.Scrub.PrivateTextCleared ||
            !report.PrivacyEvidence.Scrub.PrivateMetadataCleared ||
            !report.PrivacyEvidence.Scrub.PrivateMaterialCleared ||
            !report.PrivacyEvidence.Scrub.CollisionsDisabled ||
            !report.PrivacyEvidence.Scrub.DragTokensCleared ||
            !report.PrivacyEvidence.Scrub.TweensCancelled ||
            !report.PrivacyEvidence.Scrub.CallbacksCleared ||
            report.PrivacyEvidence.Scrub.ResolvingPrivateLeakCount != 0 ||
            report.PrivacyEvidence.Scrub.SpatialPrivateLeakCount != 0 ||
            report.PrivacyEvidence.Scrub.ForbiddenSentinelTokenCount != 0 ||
            report.Provenance.CommitSha.Length != 40 ||
            report.Provenance.ProductAssetManifest.AssetCount != 34 ||
            report.Provenance.CandidateAssetManifest.AssetCount != 1 ||
            report.Captures.Count != ExpectedStates.Length ||
            report.Captures.Any(capture =>
                capture.Revision != report.FinalRevision ||
                capture.StableFramePostDraws != 2 ||
                capture.FramePairMae > MaximumFramePairMae))
        {
            throw new InvalidOperationException(
                "The completed R3 visual-slice report violates its runtime contract.");
        }
    }

    private static void WriteReportAtomically(string reportPath, GateR3VisualSliceReport report)
    {
        string temporaryPath = $"{reportPath}.tmp-{System.Environment.ProcessId}";
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        try
        {
            File.WriteAllText(
                temporaryPath,
                json + System.Environment.NewLine,
                new UTF8Encoding(false));
            File.Move(temporaryPath, reportPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

internal sealed record GateR3VisualSliceReport
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("gate")]
    public string Gate { get; init; } = "R3";

    [JsonPropertyName("scenario")]
    public string Scenario { get; init; } = "visual-slice";

    [JsonPropertyName("arena_profile")]
    public required string ArenaProfile { get; init; }

    [JsonPropertyName("approval_status")]
    public required string ApprovalStatus { get; init; }

    [JsonPropertyName("session_setup")]
    public required GateR3SessionSetup SessionSetup { get; init; }

    [JsonPropertyName("final_revision")]
    public required ulong FinalRevision { get; init; }

    [JsonPropertyName("provenance")]
    public required GateR3BuildProvenance Provenance { get; init; }

    [JsonPropertyName("capture_contract")]
    public required GateR3CaptureContract CaptureContract { get; init; }

    [JsonPropertyName("session_evidence")]
    public required GateR3SessionEvidence SessionEvidence { get; init; }

    [JsonPropertyName("privacy_evidence")]
    public required GateR3PrivacyEvidence PrivacyEvidence { get; init; }

    [JsonPropertyName("viewport")]
    public required GateR3Viewport Viewport { get; init; }

    [JsonPropertyName("captures")]
    public required IReadOnlyList<GateR3Capture> Captures { get; init; }
}

internal sealed record GateR3SessionSetup
{
    [JsonPropertyName("seed")]
    public required uint Seed { get; init; }

    [JsonPropertyName("first_player")]
    public required int FirstPlayer { get; init; }

    [JsonPropertyName("shuffle_decks")]
    public required bool ShuffleDecks { get; init; }
}

internal sealed record GateR3CaptureContract
{
    [JsonPropertyName("frame_post_draws")]
    public int FramePostDraws { get; init; } = 2;

    [JsonPropertyName("pixel_space")]
    public string PixelSpace { get; init; } = "srgb8";

    [JsonPropertyName("maximum_frame_pair_mae")]
    public double MaximumFramePairMae { get; init; } = MaximumFramePairMaeValue;

    private const double MaximumFramePairMaeValue = 0.01;
}

internal sealed record GateR3SessionEvidence
{
    [JsonPropertyName("session_interface")]
    public required string SessionInterface { get; init; }

    [JsonPropertyName("session_runtime_type")]
    public required string SessionRuntimeType { get; init; }

    [JsonPropertyName("state_source")]
    public required string StateSource { get; init; }

    [JsonPropertyName("legal_actions_source")]
    public required string LegalActionsSource { get; init; }

    [JsonPropertyName("successful_mulligan_submissions")]
    public required int SuccessfulMulliganSubmissions { get; init; }

    [JsonPropertyName("final_legal_action_count")]
    public required int FinalLegalActionCount { get; init; }

    [JsonPropertyName("selected_action_kind")]
    public required int SelectedActionKind { get; init; }

    [JsonPropertyName("selected_source")]
    public required ulong SelectedSource { get; init; }
}

internal sealed record GateR3PrivacyEvidence
{
    [JsonPropertyName("opaque_cover_before_first_view")]
    public required bool OpaqueCoverBeforeFirstView { get; init; }

    [JsonPropertyName("viewer_request_order")]
    public required IReadOnlyList<int> ViewerRequestOrder { get; init; }

    [JsonPropertyName("explicit_reveal_count")]
    public required int ExplicitRevealCount { get; init; }

    [JsonPropertyName("snapshot_request_count")]
    public required int SnapshotRequestCount { get; init; }

    [JsonPropertyName("viewer_read_request_count")]
    public required int ViewerReadRequestCount { get; init; }

    [JsonPropertyName("premature_view_calls")]
    public required int PrematureViewCalls { get; init; }

    [JsonPropertyName("gpu_sentinel_detector_self_test_passed")]
    public required bool GpuSentinelDetectorSelfTestPassed { get; init; }

    [JsonPropertyName("injected_sentinel_exercised")]
    public required bool InjectedSentinelExercised { get; init; }

    [JsonPropertyName("injected_sentinel_runtime_scrub_verified")]
    public required bool InjectedSentinelRuntimeScrubVerified { get; init; }

    [JsonPropertyName("candidate_captures_sentinel_absent")]
    public required bool CandidateCapturesSentinelAbsent { get; init; }

    [JsonPropertyName("hidden_card_shared_back")]
    public required bool HiddenCardSharedBack { get; init; }

    [JsonPropertyName("hidden_card_count")]
    public required int HiddenCardCount { get; init; }

    [JsonPropertyName("injected_transition")]
    public required GateR3InjectedTransition InjectedTransition { get; init; }

    [JsonPropertyName("scrub")]
    public required GateR3PrivacyScrub Scrub { get; init; }
}

internal sealed record GateR3InjectedTransition
{
    [JsonPropertyName("source_action_kind")]
    public required int SourceActionKind { get; init; }

    [JsonPropertyName("source_viewer")]
    public required int SourceViewer { get; init; }

    [JsonPropertyName("source_revision")]
    public required ulong SourceRevision { get; init; }

    [JsonPropertyName("result_revision")]
    public required ulong ResultRevision { get; init; }

    [JsonPropertyName("resolving")]
    public required GateR3TransitionFrame Resolving { get; init; }

    [JsonPropertyName("covered")]
    public required GateR3TransitionFrame Covered { get; init; }
}

internal sealed record GateR3TransitionFrame
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("revision")]
    public required ulong Revision { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("complete_frame_post_draws")]
    public required int CompleteFramePostDraws { get; init; }

    [JsonPropertyName("snapshot_requests_before")]
    public required int SnapshotRequestsBefore { get; init; }

    [JsonPropertyName("snapshot_requests_after")]
    public required int SnapshotRequestsAfter { get; init; }

    [JsonPropertyName("viewer_reads_before")]
    public required int ViewerReadsBefore { get; init; }

    [JsonPropertyName("viewer_reads_after")]
    public required int ViewerReadsAfter { get; init; }

    [JsonPropertyName("privacy_sentinel_absent")]
    public required bool PrivacySentinelAbsent { get; init; }
}

internal sealed record GateR3PrivacyScrub
{
    [JsonPropertyName("private_text_cleared")]
    public required bool PrivateTextCleared { get; init; }

    [JsonPropertyName("private_metadata_cleared")]
    public required bool PrivateMetadataCleared { get; init; }

    [JsonPropertyName("private_material_cleared")]
    public required bool PrivateMaterialCleared { get; init; }

    [JsonPropertyName("collisions_disabled")]
    public required bool CollisionsDisabled { get; init; }

    [JsonPropertyName("drag_tokens_cleared")]
    public required bool DragTokensCleared { get; init; }

    [JsonPropertyName("tweens_cancelled")]
    public required bool TweensCancelled { get; init; }

    [JsonPropertyName("callbacks_cleared")]
    public required bool CallbacksCleared { get; init; }

    [JsonPropertyName("resolving_private_leak_count")]
    public required int ResolvingPrivateLeakCount { get; init; }

    [JsonPropertyName("spatial_private_leak_count")]
    public required int SpatialPrivateLeakCount { get; init; }

    [JsonPropertyName("forbidden_sentinel_token_count")]
    public required int ForbiddenSentinelTokenCount { get; init; }
}

internal sealed record GateR3BuildProvenance
{
    [JsonPropertyName("commit_sha")]
    public required string CommitSha { get; init; }

    [JsonPropertyName("commit_source")]
    public required string CommitSource { get; init; }

    [JsonPropertyName("working_tree_dirty")]
    public required bool WorkingTreeDirty { get; init; }

    [JsonPropertyName("product_asset_manifest")]
    public required GateR3ManifestIdentity ProductAssetManifest { get; init; }

    [JsonPropertyName("candidate_asset_manifest")]
    public required GateR3ManifestIdentity CandidateAssetManifest { get; init; }

    [JsonPropertyName("candidate_floor_sha256")]
    public required string CandidateFloorSha256 { get; init; }

    [JsonPropertyName("candidate_glb_sha256")]
    public required string CandidateGlbSha256 { get; init; }

    [JsonPropertyName("candidate_shader_sha256")]
    public required string CandidateShaderSha256 { get; init; }

    [JsonPropertyName("launcher_sha256")]
    public required string LauncherSha256 { get; init; }
}

internal sealed record GateR3ManifestIdentity
{
    [JsonPropertyName("resource_path")]
    public required string ResourcePath { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("asset_count")]
    public required int AssetCount { get; init; }
}

internal sealed record GateR3BuildIdentity(
    string CommitSha,
    string Source,
    bool WorkingTreeDirty);

internal sealed record GateR3Viewport
{
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }
}

internal sealed record GateR3Capture
{
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("viewer")]
    public required int Viewer { get; init; }

    [JsonPropertyName("revision")]
    public required ulong Revision { get; init; }

    [JsonPropertyName("width")]
    public required int Width { get; init; }

    [JsonPropertyName("height")]
    public required int Height { get; init; }

    [JsonPropertyName("file")]
    public required string File { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }

    [JsonPropertyName("stable_frame_post_draws")]
    public required int StableFramePostDraws { get; init; }

    [JsonPropertyName("frame_pair_mae")]
    public required double FramePairMae { get; init; }

    [JsonPropertyName("privacy_sentinel_absent")]
    public required bool PrivacySentinelAbsent { get; init; }
}
