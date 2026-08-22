using Godot;
using Scgs.Client;
using Scgs.GodotClient.Ci;
using Scgs.GodotClient.Match;
using Scgs.GodotClient.Native;
using Scgs.GodotClient.UI;
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
    private string? _ciReportPath;
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
        _ciSmoke = OS.GetCmdlineUserArgs().Contains("--ci-smoke", StringComparer.Ordinal);
        ShowMainMenu();

        try
        {
            _ciScreenshotPath = ResolveCiScreenshotPath(OS.GetCmdlineUserArgs());
            _ciReportPath = ResolveCiReportPath(OS.GetCmdlineUserArgs());
        }
        catch (Exception exception)
        {
            GD.PushError($"Gate 3C startup option validation failed: {exception}");
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
        }
        catch (Exception exception)
        {
            GD.PushError($"Gate 3C native preflight failed: {exception}");
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
            Callable.From(RunCiSmoke).CallDeferred();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
        {
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
    }

    private void RunCiSmoke()
    {
        StartMatch(MatchSetup.Defaults, deterministic: true);
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
            _match.FirstSnapshotPresented += deterministic ? OnCiSnapshotPresented : OnFirstSnapshotPresented;
            ReplaceScreen(_match);

            // Gate 3C keeps the deterministic mulligan privacy order:
            // player 0 is covered first. Begin() must not call GetView.
            _match.Begin(_session, PlayerId.Player0);
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
                Callable.From(_match.RevealForCiSmoke).CallDeferred();
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
        GD.Print($"Gate 3C first snapshot: viewer={view.Viewer}, phase={view.Phase}, revision={view.Revision}");
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
            throw new InvalidOperationException("The Gate 3C surrender phase was started more than once.");
        }
        _ciSurrenderPhaseStarted = true;
        RunCiSurrenderPhase(view);
    }

    private void BeginCiFullMatch(MatchView firstView)
    {
        if (_ciRunStarted)
        {
            throw new InvalidOperationException("The Gate 3C full-match smoke was started more than once.");
        }

        _ciRunStarted = true;
        RunCiFullMatch(firstView);
    }

    private async void RunCiFullMatch(MatchView firstView)
    {
        try
        {
            MatchScreen match = _match ??
                throw new InvalidOperationException("The Gate 3C match screen is unavailable.");
            var runner = new Gate3CFullMatchSmoke(
                match,
                NextProcessFrameAsync,
                _ciScreenshotPath);
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
                throw new InvalidOperationException("The restarted Gate 3C match screen is unavailable.");
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
            };
            if (combined.DisposedSessions < 2 || combined.ActionKinds.Count != 11)
            {
                throw new InvalidOperationException(
                    "The restart/surrender phase did not extend full-match coverage.");
            }
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

        var report = new Gate3CSmokeReport
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

    private void ReturnToMenu()
    {
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
}
