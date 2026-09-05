// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Ci;
using Scgs.GodotClient.Match;
using Scgs.GodotClient.Native;
using Scgs.GodotClient.UI;
using Scgs.GodotClient.Visuals;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Bootstrap;

/// <summary>Only the v05 product is launchable; retired prototypes are not product modes.</summary>
public sealed partial class BootstrapController : Control
{
    private Control _screenHost = null!;
    private Control? _currentScreen;
    private MainMenuScreen? _menu;
    private ProductMatchScreen? _productMatch;
    private V05.IScgsV05GameSession? _productSession;
    private ProductSmokeOptions? _productSmokeOptions;
    private ProductVisualCapture? _productCapture;
    private ProductPrivacyProbe? _productPrivacy;
    private bool _productSmokeStarted;
    private int _productSmokeMatchIndex;
    private string? _nativeLibraryPath;
    private MatchSetup? _activeSetup;
    private bool _transitionPending;
    private ColorRect? _transitionCover;

    public override void _Ready()
    {
        _screenHost = GetNode<Control>("%ScreenHost");
        IReadOnlyList<string> arguments = OS.GetCmdlineUserArgs();
        try
        {
            _productSmokeOptions = ProductSmokeOptions.Parse(arguments);
            if (_productSmokeOptions is not null)
                ProductSmokeOptions.ConfigureViewport(GetWindow(), arguments);
            if (arguments.Any(value => value == "--ci-smoke" || value == "--legacy-2d-board" ||
                value.StartsWith("--r3-visual", StringComparison.Ordinal) ||
                value.StartsWith("--ci-visual-suite", StringComparison.Ordinal) ||
                value.StartsWith("--anime-", StringComparison.Ordinal)))
                throw new ArgumentException("Retired preview/legacy modes are not product launch modes. Use --ci-product-smoke.");
            if (_productSmokeOptions?.CaptureDirectory is { } directory)
                _productCapture = new ProductVisualCapture(this, directory, _productSmokeOptions.RequirePerformance);
            if (_productSmokeOptions is { } smokeOptions)
                _productPrivacy = new ProductPrivacyProbe(this, Path.GetDirectoryName(smokeOptions.ReportPath)!);
            ShowMainMenu();
            _nativeLibraryPath = NativeLibraryLocator.ResolveProductAbsolutePath();
            // ABI/schema preflight never starts a match or reads a private viewer.
            using V05.ScgsV05GameSession probe = V05.ScgsV05GameSession.Create(
                new V05.GameConfigRequest(MatchSetup.ProductDefaults.Player0Deck,
                    MatchSetup.ProductDefaults.Player1Deck), _nativeLibraryPath);
            V05.ReactionAndChoiceResult preflight = probe.GetReactionContext(V05.PlayerId.Player0);
            if (preflight.Revision != 0 || preflight.Reaction.Pending)
                throw new InvalidOperationException("Unexpected pre-start product context.");
            _menu!.ShowAvailable();
            if (_productSmokeOptions is not null) Callable.From(RunProductMenuPrelude).CallDeferred();
        }
        catch (Exception exception)
        {
            if (_menu is null) ShowMainMenu();
            _menu!.ShowUnavailable($"本地对局暂不可用：{exception.Message}");
            if (_productSmokeOptions is not null || arguments.Contains("--ci-product-smoke"))
                FailProductSmoke(exception);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel") || _productMatch is not null) return;
        if (_menu?.HandleCancelNavigation() == true) { GetViewport().SetInputAsHandled(); return; }
        GetViewport().SetInputAsHandled();
        GetTree().Quit();
    }

    public override void _ExitTree() { _productMatch?.PrepareForSceneExit(); DisposeSession(); }

    private void ShowMainMenu()
    {
        _productMatch = null;
        _menu = GD.Load<PackedScene>("res://scenes/menu/MainMenu.tscn").Instantiate<MainMenuScreen>();
        _menu.StartRequested += StartProductMatch;
        ReplaceScreen(_menu);
        if (_nativeLibraryPath is not null) _menu.ShowAvailable();
    }

    private async void RunProductMenuPrelude()
    {
        try
        {
            MainMenuScreen menu = _menu ?? throw new InvalidOperationException("Product menu is unavailable.");
            if (_productCapture is not null)
                await _productCapture.CaptureShellAsync("menu",
                    () => ReferenceEquals(_menu, menu) && menu.GetNode<Control>("%HomePage").IsVisibleInTree());
            await ClickMenuButton(menu.GetNode<Button>("%LocalHotseatButton"));
            if (!menu.GetNode<Control>("%SetupPage").IsVisibleInTree())
                throw new InvalidOperationException("Real menu input did not open match setup.");
            if (_productCapture is not null)
                await _productCapture.CaptureShellAsync("setup",
                    () => ReferenceEquals(_menu, menu) && menu.GetNode<Control>("%SetupPage").IsVisibleInTree());
            await ClickMenuButton(menu.GetNode<Button>("%StartButton"));
            for (int frame = 0; frame < 30 && _productMatch is null; ++frame)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_productMatch is null) throw new InvalidOperationException("Real start-button input did not create a match.");
        }
        catch (Exception exception) { FailProductSmoke(exception); }
    }

    private async Task ClickMenuButton(Button button)
    {
        // Container minimum sizes/anchors settle after scene insertion, also
        // under headless where there is no draw fence to wait for.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!button.IsVisibleInTree() || button.Disabled)
            throw new InvalidOperationException("The real menu button is unavailable.");
        Vector2 position = button.GetViewport().GetFinalTransform() * button.GetGlobalRect().GetCenter();
        int windowId = checked((int)button.GetWindow().GetWindowId());
        Input.ParseInputEvent(new InputEventMouseMotion { Position = position, GlobalPosition = position, WindowId = windowId });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GD.Print($"SCGS_PRODUCT_MENU_INPUT {button.Name}: rect={button.GetGlobalRect()} window={GetWindow().Size} viewport={GetViewport().GetVisibleRect().Size} hover={GetViewport().GuiGetHoveredControl()?.Name}");
        Input.ParseInputEvent(new InputEventMouseButton
            { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left,
                ButtonMask = MouseButtonMask.Left, Pressed = true, WindowId = windowId });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventMouseButton
            { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false, WindowId = windowId });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void StartProductMatch(MatchSetup setup) => QueueScreenTransition(() => StartProductMatchNow(setup));

    private void StartProductMatchNow(MatchSetup setup)
    {
        V05.ScgsV05GameSession? candidate = null;
        try
        {
            string path = _nativeLibraryPath ?? throw new InvalidOperationException("产品原生引擎尚未通过预检。");
            var config = _productSmokeOptions?.CreateConfig(setup.Player0Deck, setup.Player1Deck, _productSmokeMatchIndex++)
                ?? new V05.GameConfigRequest(setup.Player0Deck, setup.Player1Deck);
            candidate = V05.ScgsV05GameSession.Create(config, path);
            V05.EngineStatus status = candidate.Start();
            if (!status.IsSuccess) throw new InvalidOperationException($"引擎拒绝开局：{status.Code}。");
            ProductMatchScreen screen = GD.Load<PackedScene>("res://scenes/match/ProductMatch.tscn")
                .Instantiate<ProductMatchScreen>();
            DisposeSession();
            ProductSmokeSession? audit = _productSmokeOptions is null ? null : new ProductSmokeSession(candidate);
            _productSession = audit is null ? candidate : audit;
            candidate = null;
            _menu = null;
            _activeSetup = setup;
            _productMatch = screen;
            screen.ExitRequested += ReturnToMenu;
            screen.RestartRequested += RestartMatch;
            ReplaceScreen(screen);
            screen.Begin(_productSession, MatchVisualIdentity.FromDecks(setup.Player0Deck, setup.Player1Deck));
            if (audit is not null)
            {
                screen.CiAttach(audit);
                screen.CiAttachProductPrivacy(_productPrivacy!);
                if (_productCapture is not null) screen.CiAttachProductVisual(_productCapture);
                if (!_productSmokeStarted)
                {
                    _productSmokeStarted = true;
                    Callable.From(RunProductSmoke).CallDeferred();
                }
            }
        }
        catch (Exception exception)
        {
            candidate?.Dispose();
            DisposeSession();
            if (_productSmokeOptions is not null) { FailProductSmoke(exception); return; }
            ShowMainMenu();
            _menu!.ShowError($"无法开始对局：{exception.Message}");
        }
    }

    private async void RunProductSmoke()
    {
        try
        {
            await new ProductSmokeRunner(this, () => _productMatch, _productSmokeOptions!, _productCapture).RunAsync();
            _productPrivacy!.Complete();
            GD.Print(ProductSmokeOptions.SuccessMarker);
            GetTree().Quit(0);
        }
        catch (Exception exception) { FailProductSmoke(exception); }
    }

    private void FailProductSmoke(Exception exception)
    {
        GD.PrintErr($"SCGS_PRODUCT_V05_UI_SMOKE_FAILED {exception}");
        DisposeSession();
        GetTree().Quit(1);
    }

    private void ReturnToMenu() => QueueScreenTransition(() => { DisposeSession(); ShowMainMenu(); });
    private void RestartMatch() => StartProductMatch(_activeSetup ?? MatchSetup.ProductDefaults);

    private void ReplaceScreen(Control screen)
    {
        if (_currentScreen is { } previous)
        {
            if (previous is ProductMatchScreen product)
            {
                product.ExitRequested -= ReturnToMenu;
                product.RestartRequested -= RestartMatch;
                product.PrepareForSceneExit();
            }
            previous.ProcessMode = ProcessModeEnum.Disabled;
            _screenHost.RemoveChild(previous);
            previous.QueueFree();
        }
        _currentScreen = screen;
        _screenHost.AddChild(screen);
    }

    // Native button signals must unwind before their scene can be freed.
    private void QueueScreenTransition(Action transition)
    {
        if (_transitionPending || !IsInsideTree()) return;
        _transitionPending = true;
        if (_transitionCover is null)
        {
            var layer = new CanvasLayer { Name = "SceneTransitionLayer", Layer = 100 };
            AddChild(layer);
            _transitionCover = new ColorRect
                { Name = "OpaqueTransitionCover", Color = new Color("100d23"), MouseFilter = MouseFilterEnum.Stop };
            layer.AddChild(_transitionCover);
            _transitionCover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        _transitionCover.Show();
        _productMatch?.PrepareForSceneExit();
        if (_currentScreen is not null) _currentScreen.ProcessMode = ProcessModeEnum.Disabled;
        Callable.From(() =>
        {
            if (!IsInsideTree()) return;
            try { transition(); }
            catch (Exception exception)
            {
                DisposeSession();
                ShowMainMenu();
                _menu!.ShowError($"界面切换失败：{exception.Message}");
                if (_productSmokeOptions is not null) FailProductSmoke(exception);
            }
            finally { _transitionPending = false; _transitionCover.Hide(); }
        }).CallDeferred();
    }

    private void DisposeSession() { _productSession?.Dispose(); _productSession = null; }
}
