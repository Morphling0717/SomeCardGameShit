using Godot;
using Scgs.Client;
using Scgs.GodotClient.Match;
using Scgs.GodotClient.Native;
using Scgs.GodotClient.UI;

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
    private bool _ciSmoke;

    public override void _Ready()
    {
        _screenHost = GetNode<Control>("%ScreenHost");
        _ciSmoke = OS.GetCmdlineUserArgs().Contains("--ci-smoke", StringComparer.Ordinal);
        ShowMainMenu();

        try
        {
            _ciScreenshotPath = ResolveCiScreenshotPath(OS.GetCmdlineUserArgs());
        }
        catch (Exception exception)
        {
            GD.PushError($"Gate 3A startup option validation failed: {exception}");
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
            GD.PushError($"Gate 3A native preflight failed: {exception}");
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
            _match = MatchScene.Instantiate<MatchScreen>();
            _match.ExitRequested += ReturnToMenu;
            _match.FirstSnapshotPresented += deterministic ? OnCiSnapshotPresented : OnFirstSnapshotPresented;
            ReplaceScreen(_match);

            // Gate 3A establishes a deterministic mulligan privacy order:
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
        GD.Print($"Gate 3A first snapshot: viewer={view.Viewer}, phase={view.Phase}, revision={view.Revision}");
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

            int snapshotRequestCount = _match.SnapshotRequestCount;
            if (_ciScreenshotPath is { } screenshotPath)
            {
                CaptureCiScreenshotAndExit(view, snapshotRequestCount, screenshotPath);
                return;
            }

            CompleteCiSmoke(view, snapshotRequestCount);
        }
        catch (Exception exception)
        {
            FailCiSmoke(exception.Message);
        }
    }

    private async void CaptureCiScreenshotAndExit(
        MatchView view,
        int snapshotRequestCount,
        string screenshotPath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(screenshotPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Image image = GetViewport().GetTexture().GetImage();
            Error result = image.SavePng(screenshotPath);
            if (result != Error.Ok)
            {
                throw new IOException($"Godot could not save the CI screenshot ({result}).");
            }

            GD.Print(
                $"SCGS_GODOT_CI_SCREENSHOT_OK path={screenshotPath} " +
                $"size={image.GetWidth()}x{image.GetHeight()}");
            CompleteCiSmoke(view, snapshotRequestCount);
        }
        catch (Exception exception)
        {
            FailCiSmoke(exception.Message);
        }
    }

    private void CompleteCiSmoke(MatchView view, int snapshotRequestCount)
    {
        DisposeSession();
        GD.Print(
            $"SCGS_GODOT_CI_SMOKE_OK viewer={view.Viewer} phase={view.Phase} " +
            $"revision={view.Revision} get_view_calls={snapshotRequestCount} disposed=true");
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

    private void ReturnToMenu()
    {
        DisposeSession();
        ShowMainMenu();
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
        GD.PrintErr($"SCGS_GODOT_CI_SMOKE_FAILED {message}");
        DisposeSession();
        GetTree().Quit(1);
    }
}
