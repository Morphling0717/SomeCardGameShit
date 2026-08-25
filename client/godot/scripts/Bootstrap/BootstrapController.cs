using Godot;
using Scgs.Client;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.Ci;
using Scgs.GodotClient.Match;
using Scgs.GodotClient.Native;
using Scgs.GodotClient.UI;
using Scgs.GodotClient.Visuals;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scgs.GodotClient.Bootstrap;

public sealed partial class BootstrapController : Control
{
    private const uint CiSeed = 0xC0DE_C0DEU;

    private static readonly PackedScene MainMenuScene =
        GD.Load<PackedScene>("res://scenes/menu/MainMenu.tscn");

    private static readonly PackedScene MatchScene =
        GD.Load<PackedScene>("res://scenes/match/Match.tscn");

    private Control _screenHost = null!;
    private Control? _currentScreen;
    private MainMenuScreen? _menu;
    private MatchScreen? _match;
    private ScgsGameSession? _session;
    private string? _nativeLibraryPath;
    private string? _ciScreenshotPath;
    private string? _ciActionScreenshotPath;
    private string? _ciReportPath;
    private string? _ciVisualSuitePath;
    private Gate4BVisualSuite? _ciVisualSuite;
    private string? _r3VisualSlicePath;
    private bool _r3VisualSliceExit;
    private bool _r3VisualSliceRunStarted;
    private bool _r3VisualSliceCaptureComplete;
    private bool _ciSmoke;
    private bool _ciRunStarted;
    private bool _ciTerminalSignaled;
    private bool _ciSurrenderPhaseStarted;
    private Gate3CSmokeOutcome? _ciNaturalOutcome;
    private int _ciDisposedAfterRestart;
    private MatchSetup? _activeSetup;
    private bool _activeDeterministic;

    public override void _Ready()
    {
        _screenHost = GetNode<Control>("%ScreenHost");
        IReadOnlyList<string> arguments = OS.GetCmdlineUserArgs();
        _ciSmoke = arguments.Contains("--ci-smoke", StringComparer.Ordinal) ||
                   arguments.Any(argument => argument.StartsWith(
                       "--ci-visual-suite=",
                       StringComparison.Ordinal)) ||
                   arguments.Contains("--r3-visual-slice", StringComparer.Ordinal) ||
                   arguments.Any(argument => argument.StartsWith(
                       "--r3-visual-slice=",
                       StringComparison.Ordinal)) ||
                   arguments.Contains("--r3-visual-slice-exit", StringComparer.Ordinal);
        ShowMainMenu();

        try
        {
            _ciScreenshotPath = ResolveCiScreenshotPath(arguments);
            _ciActionScreenshotPath = ResolveCiActionScreenshotPath(arguments);
            _ciReportPath = ResolveCiReportPath(arguments);
            _ciVisualSuitePath = ResolveCiVisualSuitePath(arguments);
            (_r3VisualSlicePath, _r3VisualSliceExit) =
                ResolveR3VisualSliceOptions(arguments);
            if (_ciVisualSuitePath is not null)
            {
                _ciVisualSuite = new Gate4BVisualSuite(this, _ciVisualSuitePath);
            }
        }
        catch (Exception exception)
        {
            GD.PushError($"Gate 4A startup option validation failed: {exception}");
            if (_ciSmoke)
            {
                FailCiSmoke($"启动参数无效：{exception.Message}");
                return;
            }

            _menu?.ShowUnavailable($"客户端启动参数无效：{exception.Message}");
            return;
        }

        try
        {
            _nativeLibraryPath = NativeLibraryLocator.ResolveAbsolutePath();

            // Create() performs the dynamic-library and ABI handshake. A
            // non-sensitive pre-start reaction query validates schema 1 and
            // its DTO shape without Start() or any GetView call.
            using ScgsGameSession probe = ScgsGameSession.Create(
                new GameConfigRequest(MatchSetup.Defaults.Player0Deck, MatchSetup.Defaults.Player1Deck),
                _nativeLibraryPath);
            ReactionContext preflight = probe.GetReactionContext(PlayerId.Player0);
            if (preflight.Pending || preflight.Revision != 0)
            {
                throw new ScgsProtocolException(
                    "The pre-start reaction context has unexpected state.");
            }

            _menu?.ShowAvailable();
        }
        catch (Exception exception)
        {
            GD.PushError($"Gate 4A native preflight failed: {exception}");
            if (_ciSmoke)
            {
                FailCiSmoke($"原生引擎预检失败：{exception.Message}");
                return;
            }

            _menu?.ShowUnavailable($"客户端启动失败：{exception.Message}");
            return;
        }

        if (_ciSmoke)
        {
            if (_r3VisualSlicePath is not null)
            {
                Callable.From(RunR3VisualSlice).CallDeferred();
            }
            else if (_ciVisualSuite is not null)
            {
                Callable.From(RunCiVisualSuitePrelude).CallDeferred();
            }
            else
            {
                Callable.From(RunCiSmoke).CallDeferred();
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
        {
            return;
        }

        if (_r3VisualSlicePath is not null && !_r3VisualSliceCaptureComplete)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_menu?.HandleCancelNavigation() == true)
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        GetViewport().SetInputAsHandled();
        if (_match is not null)
        {
            ReturnToMenu();
        }
        else
        {
            DisposeSession();
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        DisposeSession();
    }

    private void ShowMainMenu()
    {
        _match = null;
        _activeSetup = null;
        _activeDeterministic = false;
        _menu = MainMenuScene.Instantiate<MainMenuScreen>();
        _menu.StartRequested += setup => StartMatch(setup, deterministic: false);
        ReplaceScreen(_menu);
        if (_nativeLibraryPath is not null)
        {
            _menu.ShowAvailable();
        }
    }

    private void RunCiSmoke()
    {
        StartMatch(MatchSetup.Defaults, deterministic: true);
    }

    private void RunR3VisualSlice()
    {
        StartMatch(MatchSetup.Defaults, deterministic: true);
    }

    private async void RunCiVisualSuitePrelude()
    {
        try
        {
            Gate4BVisualSuite suite = _ciVisualSuite ??
                throw new InvalidOperationException("The Gate 4B visual suite is unavailable.");
            MainMenuScreen menu = _menu ??
                throw new InvalidOperationException("The product menu is unavailable.");
            menu.ShowRootForCi();
            await suite.CaptureAsync("menu");
            menu.ShowLocalSetupForCi();
            await suite.CaptureAsync("match-setup");
            menu.ShowErrorForCi("视觉套件：受控错误页示例（未访问原生会话）");
            await suite.CaptureAsync("error");
            menu.ShowLocalSetupForCi();
            StartMatch(MatchSetup.Defaults, deterministic: true);
        }
        catch (Exception exception)
        {
            FailCiSmoke(exception.Message);
        }
    }

    private void StartMatch(MatchSetup setup, bool deterministic)
    {
        ScgsGameSession? candidate = null;
        try
        {
            string nativeLibraryPath = _nativeLibraryPath ??
                throw new InvalidOperationException("原生引擎尚未通过启动预检。");
            var config = new GameConfigRequest(setup.Player0Deck, setup.Player1Deck);
            if (deterministic)
            {
                config = config with
                {
                    RandomSeed = CiSeed,
                    FirstPlayerMode = FirstPlayerMode.Player0,
                    ShuffleDecks = false,
                };
            }

            candidate = ScgsGameSession.Create(config, nativeLibraryPath);
            EngineStatus started = candidate.Start();
            if (!started.IsSuccess)
            {
                candidate.Dispose();
                throw new InvalidOperationException($"引擎拒绝开始比赛：{started.Code}（{started.RawCode}）。");
            }

            DisposeSession();
            _session = candidate;
            candidate = null;
            _menu = null;
            _activeSetup = setup;
            _activeDeterministic = deterministic;
            _match = MatchScene.Instantiate<MatchScreen>();
            _match.ExitRequested += ReturnToMenu;
            _match.RestartRequested += RestartMatch;
            if (_r3VisualSlicePath is null)
            {
                _match.FirstSnapshotPresented += deterministic
                    ? OnCiSnapshotPresented
                    : OnFirstSnapshotPresented;
            }
            ReplaceScreen(_match);

            if (_r3VisualSlicePath is not null)
            {
                _match.UseVisualProfile(BattlefieldVisualProfile.R3Candidate);
            }

            // Gate 3C keeps the deterministic mulligan privacy order:
            // player 0 is covered first. Begin() must not call GetView.
            _match.Begin(
                _session,
                PlayerId.Player0,
                MatchVisualIdentity.FromDecks(setup.Player0Deck, setup.Player1Deck));
            if (_match.SnapshotRequestCount != 0 || !_match.IsPrivacyCoverVisible)
            {
                throw new InvalidOperationException("Privacy invariant failed before the reveal request.");
            }

            if (_match.OpponentHandBackCount != 0)
            {
                throw new InvalidOperationException("Opponent hand backs were visible before the reveal request.");
            }

            if (deterministic)
            {
                if (_r3VisualSlicePath is not null)
                {
                    Callable.From(RunR3VisualSliceDriver).CallDeferred();
                }
                else
                {
                    Callable.From(_match.RevealForCiSmoke).CallDeferred();
                }
            }
        }
        catch (Exception exception)
        {
            GD.PushError(exception.ToString());
            candidate?.Dispose();
            DisposeSession();
            if (_ciSmoke)
            {
                FailCiSmoke(exception.Message);
                return;
            }

            if (_menu is null)
            {
                ShowMainMenu();
            }

            _menu?.ShowError($"无法创建比赛：{exception.Message}");
        }
    }

    private static void OnFirstSnapshotPresented(MatchView view)
    {
        GD.Print($"Gate 4A first snapshot: viewer={view.Viewer}, phase={view.Phase}, revision={view.Revision}");
    }

    private void OnCiSnapshotPresented(MatchView view)
    {
        try
        {
            if (_match is null || !_match.HasPresentedSnapshot || _match.IsPrivacyCoverVisible)
            {
                throw new InvalidOperationException("The structured snapshot was not visibly presented.");
            }

            if (_match.SnapshotRequestCount != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one post-reveal GetView call, saw {_match.SnapshotRequestCount}.");
            }

            if (view.Viewer != PlayerId.Player0 || view.Phase != MatchPhase.Mulligan ||
                view.FirstPlayer != PlayerId.Player0 || view.RandomSeed != CiSeed)
            {
                throw new InvalidOperationException("The deterministic first snapshot has unexpected match metadata.");
            }

            if (view.Players.Length != 2 || view.Players[0].Hand.Length == 0 || view.Players[1].Hand.Length != 0)
            {
                throw new InvalidOperationException("The first snapshot does not satisfy the viewer privacy contract.");
            }

            int expectedOpponentBacks = checked((int)view.Players[1].HandCount);
            if (_match.OpponentHandBackCount != expectedOpponentBacks ||
                _match.OpponentHandBackCount == 0)
            {
                throw new InvalidOperationException("Opponent hand backs do not match the identity-free hand count.");
            }

            if (!_match.RenderedLabelsMatch(view))
            {
                throw new InvalidOperationException("Rendered labels do not match the structured DTO snapshot.");
            }

            ContinueCiFromFirstSnapshot(view);
        }
        catch (Exception exception)
        {
            FailCiSmoke(exception.Message);
        }
    }

    private void ContinueCiFromFirstSnapshot(MatchView view)
    {
        if (_ciNaturalOutcome is null)
        {
            BeginCiFullMatch(view);
            return;
        }

        if (_ciSurrenderPhaseStarted)
        {
            throw new InvalidOperationException("The Gate 4A surrender phase was started more than once.");
        }
        _ciSurrenderPhaseStarted = true;
        RunCiSurrenderPhase(view);
    }

    private void BeginCiFullMatch(MatchView firstView)
    {
        if (_ciRunStarted)
        {
            throw new InvalidOperationException("The Gate 4A full-match smoke was started more than once.");
        }

        _ciRunStarted = true;
        RunCiFullMatch(firstView);
    }

    private async void RunCiFullMatch(MatchView firstView)
    {
        try
        {
            MatchScreen match = _match ??
                throw new InvalidOperationException("The Gate 4A match screen is unavailable.");
            var runner = new Gate3CFullMatchSmoke(
                match,
                NextProcessFrameAsync,
                _ciScreenshotPath,
                _ciActionScreenshotPath,
                _ciVisualSuite);
            Gate3CSmokeOutcome outcome = await runner.RunAsync();
            if (outcome.FinalView.RandomSeed != firstView.RandomSeed ||
                outcome.FinalView.FirstPlayer != firstView.FirstPlayer ||
                outcome.FinalView.Result == GameResult.Ongoing ||
                outcome.Steps <= 2)
            {
                throw new InvalidOperationException(
                    "The terminal smoke outcome does not match the deterministic first snapshot.");
            }

            _ciNaturalOutcome = outcome;
            MatchScreen completedMatch = match;
            completedMatch.RestartThroughResultSignalForCi();
            await NextProcessFrameAsync();
            _ciDisposedAfterRestart = completedMatch.CiDisposedSessionCount;
            if (_ciDisposedAfterRestart < 1 || ReferenceEquals(_match, completedMatch))
            {
                throw new InvalidOperationException(
                    "The result-overlay restart signal did not replace and dispose the first match.");
            }
        }
        catch (Exception exception)
        {
            FailCiSmoke(exception.Message);
        }
    }

    private async void RunCiSurrenderPhase(MatchView firstView)
    {
        try
        {
            Gate3CSmokeOutcome natural = _ciNaturalOutcome ??
                throw new InvalidOperationException("The natural terminal outcome is unavailable.");
            MatchScreen match = _match ??
                throw new InvalidOperationException("The restarted Gate 4A match screen is unavailable.");
            if (firstView.RandomSeed != natural.FinalView.RandomSeed ||
                firstView.FirstPlayer != natural.FinalView.FirstPlayer ||
                firstView.Phase != MatchPhase.Mulligan)
            {
                throw new InvalidOperationException(
                    "The signal-restarted match does not preserve deterministic setup.");
            }

            var runner = new Gate3CSurrenderSmoke(match, NextProcessFrameAsync);
            Gate3CSurrenderOutcome surrender = await runner.RunAsync();
            ActionKind[] actionKinds = natural.ActionKinds
                .Append(ActionKind.Surrender)
                .Distinct()
                .OrderBy(action => (uint)action)
                .ToArray();
            var combined = natural with
            {
                FinalView = surrender.FinalView,
                Steps = natural.Steps + surrender.Steps,
                ActionKinds = actionKinds,
                Covers = natural.Covers + surrender.Covers,
                Reveals = natural.Reveals + surrender.Reveals,
                PrematureViewerCalls = natural.PrematureViewerCalls + surrender.PrematureViewerCalls,
                DisposedSessions = _ciDisposedAfterRestart + surrender.DisposedSessions,
                ResolvingPublicFrames = Math.Min(
                    natural.ResolvingPublicFrames,
                    surrender.ResolvingPublicFrames),
                ResolvingPrivateLeaks = natural.ResolvingPrivateLeaks + surrender.ResolvingPrivateLeaks,
                Restarts = 1,
                SurrenderTerminals = 1,
                SurfaceIntentE2e = natural.SurfaceIntentE2e || match.CiSurfaceIntentE2e,
                RaycastE2e = natural.RaycastE2e || match.CiRaycastE2e,
                HudRaycastBlocks = natural.HudRaycastBlocks + match.CiHudRaycastBlocks,
                PerspectiveRebuilds = natural.PerspectiveRebuilds + match.CiPerspectiveRebuilds,
                ActorPoolReuses = natural.ActorPoolReuses + match.CiActorPoolReuses,
                BlockedSpatialInputs = natural.BlockedSpatialInputs + match.CiBlockedSpatialInputs,
                SpatialPrivateLeaks = natural.SpatialPrivateLeaks + match.CiSpatialPrivateLeaks,
            };
            if (combined.DisposedSessions < 2 || combined.ActionKinds.Count != 11)
            {
                throw new InvalidOperationException(
                    "The restart/surrender phase did not extend full-match coverage.");
            }
            _ciVisualSuite?.Complete();
            WriteCiReport(combined);
            CompleteCiSmoke(combined);
        }
        catch (Exception exception)
        {
            FailCiSmoke(exception.Message);
        }
    }

    private async Task NextProcessFrameAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void WriteCiReport(Gate3CSmokeOutcome outcome)
    {
        if (_ciReportPath is not { } reportPath)
        {
            return;
        }

        MatchSetup setup = _activeSetup ??
            throw new InvalidOperationException("The active deck setup is unavailable for the CI report.");
        string? directory = Path.GetDirectoryName(reportPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var report = new Gate4ASmokeReport
        {
            Seed = outcome.FinalView.RandomSeed,
            Player0Deck = setup.Player0Deck,
            Player1Deck = setup.Player1Deck,
            FirstPlayer = checked((int)(uint)outcome.FinalView.FirstPlayer),
            Steps = outcome.Steps,
            Turns = outcome.Turns,
            ActionKinds = outcome.ActionKinds
                .Select(action => checked((int)(uint)action))
                .Order()
                .ToArray(),
            Covers = outcome.Covers,
            Reveals = outcome.Reveals,
            PrematureViewCalls = outcome.PrematureViewerCalls,
            SignalE2e = outcome.SignalE2e,
            ClickDragCanonicalParity = outcome.ClickDragCanonicalParity,
            SelectionCommitWithoutConfirmation = outcome.SelectionCommitWithoutConfirmation,
            ResolvingPublicFrames = outcome.ResolvingPublicFrames,
            ResolvingPrivateLeaks = outcome.ResolvingPrivateLeaks,
            Restarts = outcome.Restarts,
            SurrenderTerminals = outcome.SurrenderTerminals,
            Result = checked((int)(uint)outcome.FinalView.Result),
            DisposedSessions = outcome.DisposedSessions,
            PresentationMode = outcome.PresentationMode,
            SurfaceIntentE2e = outcome.SurfaceIntentE2e,
            RaycastE2e = outcome.RaycastE2e,
            HudRaycastBlocks = outcome.HudRaycastBlocks,
            DragThresholdPixels = outcome.DragThresholdPixels,
            CameraFovDegrees = outcome.CameraFovDegrees,
            CameraPitchDegrees = outcome.CameraPitchDegrees,
            PerspectiveRebuilds = outcome.PerspectiveRebuilds,
            ActorPoolReuses = outcome.ActorPoolReuses,
            BlockedSpatialInputs = outcome.BlockedSpatialInputs,
            SpatialPrivateLeaks = outcome.SpatialPrivateLeaks,
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
        string temporaryPath = $"{reportPath}.tmp-{System.Environment.ProcessId}";
        try
        {
            File.WriteAllText(temporaryPath, json + System.Environment.NewLine, new UTF8Encoding(false));
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

    private void CompleteCiSmoke(Gate3CSmokeOutcome outcome)
    {
        if (_ciTerminalSignaled)
        {
            return;
        }

        _ciTerminalSignaled = true;
        DisposeSession();
        GD.Print(
            $"SCGS_GODOT_CI_SMOKE_OK result={outcome.FinalView.Result} " +
            $"revision={outcome.FinalView.Revision} steps={outcome.Steps} " +
            $"covers={outcome.Covers} reveals={outcome.Reveals} " +
            $"premature_view_calls={outcome.PrematureViewerCalls} disposed=true");
        GetTree().Quit(0);
    }

    private string? ResolveCiScreenshotPath(IReadOnlyList<string> arguments)
    {
        const string prefix = "--ci-screenshot=";
        string[] values = arguments
            .Where(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
            .Select(argument => argument[prefix.Length..])
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        if (!_ciSmoke)
        {
            throw new InvalidOperationException("--ci-screenshot requires --ci-smoke.");
        }

        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]) ||
            !Path.IsPathFullyQualified(values[0]))
        {
            throw new InvalidOperationException("--ci-screenshot requires one absolute output path.");
        }

        return Path.GetFullPath(values[0]);
    }

    private string? ResolveCiActionScreenshotPath(IReadOnlyList<string> arguments)
    {
        const string prefix = "--ci-action-screenshot=";
        string[] values = arguments
            .Where(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
            .Select(argument => argument[prefix.Length..])
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        if (!_ciSmoke)
        {
            throw new InvalidOperationException("--ci-action-screenshot requires --ci-smoke.");
        }

        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]) ||
            !Path.IsPathFullyQualified(values[0]))
        {
            throw new InvalidOperationException(
                "--ci-action-screenshot requires one absolute output path.");
        }

        return Path.GetFullPath(values[0]);
    }

    private string? ResolveCiReportPath(IReadOnlyList<string> arguments)
    {
        const string prefix = "--ci-report=";
        string[] values = arguments
            .Where(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
            .Select(argument => argument[prefix.Length..])
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        if (!_ciSmoke)
        {
            throw new InvalidOperationException("--ci-report requires --ci-smoke.");
        }

        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]) ||
            !Path.IsPathFullyQualified(values[0]))
        {
            throw new InvalidOperationException("--ci-report requires one absolute output path.");
        }

        return Path.GetFullPath(values[0]);
    }

    private static string? ResolveCiVisualSuitePath(IReadOnlyList<string> arguments)
    {
        const string prefix = "--ci-visual-suite=";
        string[] values = arguments
            .Where(argument => argument.StartsWith(prefix, StringComparison.Ordinal))
            .Select(argument => argument[prefix.Length..])
            .ToArray();
        if (values.Length == 0)
        {
            return null;
        }
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0]) ||
            !Path.IsPathFullyQualified(values[0]))
        {
            throw new InvalidOperationException(
                "--ci-visual-suite requires one absolute output directory.");
        }
        if (OS.GetCmdlineUserArgs().Contains("--legacy-2d-board", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "--ci-visual-suite is available only for the default 3D product presentation.");
        }
        return Path.GetFullPath(values[0]);
    }

    private static (string? OutputDirectory, bool ExitWhenReady)
        ResolveR3VisualSliceOptions(IReadOnlyList<string> arguments)
    {
        const string option = "--r3-visual-slice";
        const string prefix = "--r3-visual-slice=";
        const string exitOption = "--r3-visual-slice-exit";
        string[] modes = arguments
            .Where(argument => argument == option ||
                               argument.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
        int exitCount = arguments.Count(argument => argument == exitOption);
        if (modes.Length == 0)
        {
            if (exitCount != 0)
            {
                throw new InvalidOperationException(
                    "--r3-visual-slice-exit requires --r3-visual-slice.");
            }
            return (null, false);
        }
        if (modes.Length != 1)
        {
            throw new InvalidOperationException(
                "--r3-visual-slice may be specified only once.");
        }
        if (exitCount > 1)
        {
            throw new InvalidOperationException(
                "--r3-visual-slice-exit may be specified only once.");
        }
        if (arguments.Contains("--ci-smoke", StringComparer.Ordinal) ||
            arguments.Any(argument => argument.StartsWith(
                "--ci-visual-suite=",
                StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "--r3-visual-slice cannot be combined with the Gate 4B-R2 CI modes.");
        }
        if (arguments.Contains("--legacy-2d-board", StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "--r3-visual-slice requires the default 3D presentation.");
        }

        string path;
        if (modes[0] == option)
        {
            path = ProjectSettings.GlobalizePath("user://r3-visual-slice");
        }
        else
        {
            path = modes[0][prefix.Length..];
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            {
                throw new InvalidOperationException(
                    "--r3-visual-slice=<directory> requires one absolute output directory.");
            }
        }

        return (Path.GetFullPath(path), exitCount == 1);
    }

    private async void RunR3VisualSliceDriver()
    {
        if (_r3VisualSliceRunStarted)
        {
            FailCiSmoke("The R3 visual-slice driver was started more than once.");
            return;
        }
        _r3VisualSliceRunStarted = true;

        try
        {
            MatchScreen match = _match ??
                throw new InvalidOperationException("The R3 match screen is unavailable.");
            IScgsGameSession session = _session ??
                throw new InvalidOperationException("The R3 native session is unavailable.");
            string outputDirectory = _r3VisualSlicePath ??
                throw new InvalidOperationException("The R3 output directory is unavailable.");
            var collector = new GateR3VisualSlice(
                this,
                match,
                session,
                outputDirectory,
                NextProcessFrameAsync);
            string reportPath = await collector.RunAsync();
            _r3VisualSliceCaptureComplete = true;
            GD.Print(
                $"SCGS_R3_VISUAL_SLICE_READY report={reportPath} " +
                "approval_status=pending_user_approval");
            if (_r3VisualSliceExit)
            {
                _ciTerminalSignaled = true;
                DisposeSession();
                GetTree().Quit(0);
            }
        }
        catch (Exception exception)
        {
            FailCiSmoke(exception.Message);
        }
    }

    private void ReturnToMenu()
    {
        if (_r3VisualSlicePath is not null && !_r3VisualSliceCaptureComplete)
        {
            return;
        }

        DisposeSession();
        ShowMainMenu();
    }

    private void RestartMatch()
    {
        MatchSetup setup = _activeSetup ?? MatchSetup.Defaults;
        bool deterministic = _activeDeterministic;
        StartMatch(setup, deterministic);
    }

    private void ReplaceScreen(Control screen)
    {
        _currentScreen?.Free();
        _currentScreen = screen;
        _screenHost.AddChild(screen);
    }

    private void DisposeSession()
    {
        _session?.Dispose();
        _session = null;
    }

    private void FailCiSmoke(string message)
    {
        if (_ciTerminalSignaled)
        {
            return;
        }

        _ciTerminalSignaled = true;
        GD.PrintErr($"SCGS_GODOT_CI_SMOKE_FAILED {message}");
        DisposeSession();
        GetTree().Quit(1);
    }

    private sealed record Gate3CSmokeReport
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; } = 2;

        [JsonPropertyName("gate")]
        public string Gate { get; init; } = "3C";

        [JsonPropertyName("scenario")]
        public string Scenario { get; init; } = "full-match";

        [JsonPropertyName("seed")]
        public required uint Seed { get; init; }

        [JsonPropertyName("player0_deck")]
        public required string Player0Deck { get; init; }

        [JsonPropertyName("player1_deck")]
        public required string Player1Deck { get; init; }

        [JsonPropertyName("first_player")]
        public required int FirstPlayer { get; init; }

        [JsonPropertyName("steps")]
        public required int Steps { get; init; }

        [JsonPropertyName("turns")]
        public required int Turns { get; init; }

        [JsonPropertyName("action_kinds")]
        public required IReadOnlyList<int> ActionKinds { get; init; }

        [JsonPropertyName("covers")]
        public required int Covers { get; init; }

        [JsonPropertyName("reveals")]
        public required int Reveals { get; init; }

        [JsonPropertyName("premature_view_calls")]
        public required int PrematureViewCalls { get; init; }

        [JsonPropertyName("signal_e2e")]
        public required bool SignalE2e { get; init; }

        [JsonPropertyName("click_drag_canonical_parity")]
        public required bool ClickDragCanonicalParity { get; init; }

        [JsonPropertyName("selection_commit_without_confirmation")]
        public required bool SelectionCommitWithoutConfirmation { get; init; }

        [JsonPropertyName("resolving_public_frames")]
        public required int ResolvingPublicFrames { get; init; }

        [JsonPropertyName("resolving_private_leaks")]
        public required int ResolvingPrivateLeaks { get; init; }

        [JsonPropertyName("restarts")]
        public required int Restarts { get; init; }

        [JsonPropertyName("surrender_terminals")]
        public required int SurrenderTerminals { get; init; }

        [JsonPropertyName("result")]
        public required int Result { get; init; }

        [JsonPropertyName("disposed_sessions")]
        public required int DisposedSessions { get; init; }
    }

    private sealed record Gate4ASmokeReport
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; } = 3;

        [JsonPropertyName("gate")]
        public string Gate { get; init; } = "4A";

        [JsonPropertyName("scenario")]
        public string Scenario { get; init; } = "full-match";

        [JsonPropertyName("seed")]
        public required uint Seed { get; init; }

        [JsonPropertyName("player0_deck")]
        public required string Player0Deck { get; init; }

        [JsonPropertyName("player1_deck")]
        public required string Player1Deck { get; init; }

        [JsonPropertyName("first_player")]
        public required int FirstPlayer { get; init; }

        [JsonPropertyName("steps")]
        public required int Steps { get; init; }

        [JsonPropertyName("turns")]
        public required int Turns { get; init; }

        [JsonPropertyName("action_kinds")]
        public required IReadOnlyList<int> ActionKinds { get; init; }

        [JsonPropertyName("covers")]
        public required int Covers { get; init; }

        [JsonPropertyName("reveals")]
        public required int Reveals { get; init; }

        [JsonPropertyName("premature_view_calls")]
        public required int PrematureViewCalls { get; init; }

        [JsonPropertyName("signal_e2e")]
        public required bool SignalE2e { get; init; }

        [JsonPropertyName("click_drag_canonical_parity")]
        public required bool ClickDragCanonicalParity { get; init; }

        [JsonPropertyName("selection_commit_without_confirmation")]
        public required bool SelectionCommitWithoutConfirmation { get; init; }

        [JsonPropertyName("resolving_public_frames")]
        public required int ResolvingPublicFrames { get; init; }

        [JsonPropertyName("resolving_private_leaks")]
        public required int ResolvingPrivateLeaks { get; init; }

        [JsonPropertyName("restarts")]
        public required int Restarts { get; init; }

        [JsonPropertyName("surrender_terminals")]
        public required int SurrenderTerminals { get; init; }

        [JsonPropertyName("result")]
        public required int Result { get; init; }

        [JsonPropertyName("disposed_sessions")]
        public required int DisposedSessions { get; init; }

        [JsonPropertyName("presentation_mode")]
        public required string PresentationMode { get; init; }

        [JsonPropertyName("surface_intent_e2e")]
        public required bool SurfaceIntentE2e { get; init; }

        [JsonPropertyName("raycast_e2e")]
        public required bool RaycastE2e { get; init; }

        [JsonPropertyName("hud_raycast_blocks")]
        public required int HudRaycastBlocks { get; init; }

        [JsonPropertyName("drag_threshold_pixels")]
        public required int DragThresholdPixels { get; init; }

        [JsonPropertyName("camera_fov_degrees")]
        public required int CameraFovDegrees { get; init; }

        [JsonPropertyName("camera_pitch_degrees")]
        public required int CameraPitchDegrees { get; init; }

        [JsonPropertyName("perspective_rebuilds")]
        public required int PerspectiveRebuilds { get; init; }

        [JsonPropertyName("actor_pool_reuses")]
        public required int ActorPoolReuses { get; init; }

        [JsonPropertyName("blocked_spatial_inputs")]
        public required int BlockedSpatialInputs { get; init; }

        [JsonPropertyName("spatial_private_leaks")]
        public required int SpatialPrivateLeaks { get; init; }
    }
}
