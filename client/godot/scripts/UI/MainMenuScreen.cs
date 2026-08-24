using Godot;
using Scgs.GodotClient.Match;
using Scgs.GodotClient.Visual;

namespace Scgs.GodotClient.UI;

public sealed partial class MainMenuScreen : Control
{
    private const string MenuBackgroundPath =
        "res://assets/visual/menu/gate4b-menu-background.png";

    private static readonly (string Key, string Label)[] FixedDecks =
    [
        ("midrange", "常规中速"),
        ("advance", "预支实验"),
    ];

    private static readonly Dictionary<string, (string Faction, string Description)> DeckCopy =
        new(StringComparer.Ordinal)
        {
            ["midrange"] = (
                "蓝钢军事 · 稳定推进",
                "以侦察、护卫与指挥协同建立场面，依靠精确交换和重型战备结束对局。"),
            ["advance"] = (
                "紫金工业 · 能量债务",
                "借预支、燃耗与裂痕换取爆发节奏，再以债务装甲和巨型机枢收束战局。"),
        };

    private Control _homePage = null!;
    private Control _setupPage = null!;
    private Control _settingsPage = null!;
    private Control _errorPage = null!;
    private OptionButton _player0Deck = null!;
    private OptionButton _player1Deck = null!;
    private Label _player0Faction = null!;
    private Label _player0Preview = null!;
    private Label _player1Faction = null!;
    private Label _player1Preview = null!;
    private Label _errorLabel = null!;
    private Label _availabilityLabel = null!;
    private Label _featureNotice = null!;
    private Label _errorPageMessage = null!;
    private OptionButton _windowModeOption = null!;
    private OptionButton _resolutionOption = null!;
    private OptionButton _uiScaleOption = null!;
    private CheckButton _vsyncCheck = null!;
    private CheckButton _reduceMotionCheck = null!;
    private Label _settingsStatus = null!;
    private Button _localHotseatButton = null!;
    private Button _startButton = null!;
    private AcceptDialog _aboutDialog = null!;
    private IVisualSettingsStore _settingsStore = null!;
    private ClientVisualSettings _settings = ClientVisualSettings.Defaults;
    private MenuPage _page = MenuPage.Home;
    private Tween? _pageTween;
    private bool _nativeAvailable;
    private bool _busy;
    private bool _automationRun;

    public event Action<MatchSetup>? StartRequested;

    public event Action<ClientVisualSettings>? VisualSettingsChanged;

    public override void _Ready()
    {
        BindNodes();
        PopulateDecks(_player0Deck, defaultIndex: 0);
        PopulateDecks(_player1Deck, defaultIndex: 1);
        PopulateSettingsOptions();
        WireSignals();
        LoadOptionalBackground();

        _automationRun = IsAutomationRun(OS.GetCmdlineUserArgs());
        _settingsStore = new GodotVisualSettingsStore();
        _settings = _automationRun
            ? ClientVisualSettings.Defaults
            : _settingsStore.Load();
        ClientVisualSettingsRuntime.SetCurrent(_settings);
        if (!_automationRun)
        {
            ClientVisualSettingsApplier.Apply(GetWindow(), _settings);
        }

        BindSettings(_settings);
        RefreshDeckPreview(_player0Deck, _player0Faction, _player0Preview);
        RefreshDeckPreview(_player1Deck, _player1Faction, _player1Preview);
        ShowPage(MenuPage.Home);
        UpdateAvailability();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!@event.IsActionPressed("ui_cancel"))
        {
            return;
        }

        if (HandleCancelNavigation())
        {
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        _pageTween?.Kill();
        _pageTween = null;
    }

    public bool HandleCancelNavigation()
    {
        if (_aboutDialog.Visible)
        {
            _aboutDialog.Hide();
            return true;
        }

        if (_page == MenuPage.Home)
        {
            return false;
        }

        ShowPage(MenuPage.Home);
        return true;
    }

    public void SetBusy(bool busy)
    {
        _busy = busy;
        _player0Deck.Disabled = busy;
        _player1Deck.Disabled = busy;
        UpdateAvailability();
    }

    public void ShowAvailable()
    {
        _nativeAvailable = true;
        _featureNotice.Text = "本地热座已可用；其他作战模块正在开发中。";
        UpdateAvailability();
    }

    public void ShowError(string message)
    {
        SetBusy(false);
        ShowPage(MenuPage.Setup);
        _errorLabel.Text = message;
        _errorLabel.Visible = true;
    }

    public void ShowUnavailable(string message)
    {
        _busy = false;
        _nativeAvailable = false;
        _errorLabel.Visible = false;
        _featureNotice.Text = "本地热座已禁用；菜单、设置与制作信息仍可使用。";
        _availabilityLabel.Text = $"● 对战核心不可用\n{message}";
        _availabilityLabel.Modulate = new Color(1.0f, 0.58f, 0.52f);
        ShowPage(MenuPage.Home);
        UpdateAvailability(preserveMessage: true);
    }

    public void ShowRootForCi()
    {
        _featureNotice.Text = "本地热座已可用；其他作战模块正在开发中。";
        ShowPage(MenuPage.Home);
    }

    public void ShowLocalSetupForCi()
    {
        ShowPage(MenuPage.Setup);
    }

    public void ShowErrorForCi(string message)
    {
        _errorPageMessage.Text = message;
        ShowPage(MenuPage.Error);
    }

    private void BindNodes()
    {
        _homePage = GetNode<Control>("%HomePage");
        _setupPage = GetNode<Control>("%SetupPage");
        _settingsPage = GetNode<Control>("%SettingsPage");
        _errorPage = GetNode<Control>("%ErrorPage");
        _player0Deck = GetNode<OptionButton>("%Player0Deck");
        _player1Deck = GetNode<OptionButton>("%Player1Deck");
        _player0Faction = GetNode<Label>("%Player0Faction");
        _player0Preview = GetNode<Label>("%Player0Preview");
        _player1Faction = GetNode<Label>("%Player1Faction");
        _player1Preview = GetNode<Label>("%Player1Preview");
        _errorLabel = GetNode<Label>("%ErrorLabel");
        _availabilityLabel = GetNode<Label>("%AvailabilityLabel");
        _featureNotice = GetNode<Label>("%FeatureNotice");
        _errorPageMessage = GetNode<Label>("%ErrorPageMessage");
        _windowModeOption = GetNode<OptionButton>("%WindowModeOption");
        _resolutionOption = GetNode<OptionButton>("%ResolutionOption");
        _uiScaleOption = GetNode<OptionButton>("%UiScaleOption");
        _vsyncCheck = GetNode<CheckButton>("%VSyncCheck");
        _reduceMotionCheck = GetNode<CheckButton>("%ReduceMotionCheck");
        _settingsStatus = GetNode<Label>("%SettingsStatus");
        _localHotseatButton = GetNode<Button>("%LocalHotseatButton");
        _startButton = GetNode<Button>("%StartButton");
        _aboutDialog = GetNode<AcceptDialog>("%AboutDialog");
    }

    private void WireSignals()
    {
        _localHotseatButton.Pressed += () => ShowPage(MenuPage.Setup);
        GetNode<Button>("%SetupBackButton").Pressed += () => ShowPage(MenuPage.Home);
        _startButton.Pressed += OnStartPressed;
        _player0Deck.ItemSelected += _ =>
            RefreshDeckPreview(_player0Deck, _player0Faction, _player0Preview);
        _player1Deck.ItemSelected += _ =>
            RefreshDeckPreview(_player1Deck, _player1Faction, _player1Preview);

        WireDevelopmentButton("%SinglePlayerButton", ProductMenuFeature.SinglePlayer);
        WireDevelopmentButton("%OnlineButton", ProductMenuFeature.OnlinePlay);
        WireDevelopmentButton("%DeckEditorButton", ProductMenuFeature.DeckEditor);
        WireDevelopmentButton("%CardLibraryButton", ProductMenuFeature.CardLibrary);
        WireDevelopmentButton("%ReplayButton", ProductMenuFeature.ReplayViewer);

        GetNode<Button>("%SettingsButton").Pressed += OpenSettings;
        GetNode<Button>("%SettingsBackButton").Pressed += () => ShowPage(MenuPage.Home);
        GetNode<Button>("%SaveSettingsButton").Pressed += SaveSettings;
        _windowModeOption.ItemSelected += _ => RefreshResolutionAvailability();
        GetNode<Button>("%QuitButton").Pressed += () => GetTree().Quit();
        GetNode<Button>("%AboutButton").Pressed += () =>
            _aboutDialog.PopupCentered(new Vector2I(640, 410));
        GetNode<Button>("%ErrorReturnButton").Pressed += () => ShowPage(MenuPage.Home);
    }

    private void WireDevelopmentButton(string nodePath, ProductMenuFeature feature)
    {
        ProductMenuEntry entry = ProductMenuCatalog.Get(feature);
        if (entry.Status != ProductMenuFeatureStatus.InDevelopment ||
            entry.RequiresNativeSession)
        {
            throw new InvalidOperationException(
                $"{entry.Label} is not a safe development-only menu entry.");
        }

        GetNode<Button>(nodePath).Pressed += () =>
        {
            _featureNotice.Text = $"{entry.Label}：开发中，敬请期待。";
        };
    }

    private void LoadOptionalBackground()
    {
        if (!ResourceLoader.Exists(MenuBackgroundPath, "Texture2D"))
        {
            return;
        }

        Texture2D? texture = ResourceLoader.Load<Texture2D>(MenuBackgroundPath);
        if (texture is not null)
        {
            GetNode<TextureRect>("%MenuBackground").Texture = texture;
        }
    }

    private void OpenSettings()
    {
        BindSettings(_settings);
        _settingsStatus.Text = string.Empty;
        ShowPage(MenuPage.Settings);
    }

    private void SaveSettings()
    {
        ClientVisualSettings candidate = ReadSettings().Normalize();
        try
        {
            if (!_automationRun)
            {
                _settingsStore.Save(candidate);
                ClientVisualSettingsApplier.Apply(GetWindow(), candidate);
            }
            else
            {
                ClientVisualSettingsRuntime.SetCurrent(candidate);
            }

            _settings = candidate;
            BindSettings(_settings);
            _settingsStatus.Text = "设置已保存并应用。";
            VisualSettingsChanged?.Invoke(_settings);
        }
        catch (Exception exception)
        {
            GD.PushError($"Failed to save visual settings: {exception}");
            _settingsStatus.Text = $"无法保存设置：{exception.Message}";
        }
    }

    private void PopulateSettingsOptions()
    {
        _windowModeOption.Clear();
        AddOption(_windowModeOption, "窗口模式", (int)ClientWindowMode.Windowed);
        AddOption(_windowModeOption, "无边框全屏", (int)ClientWindowMode.BorderlessFullscreen);

        _resolutionOption.Clear();
        foreach (ClientResolution resolution in ClientVisualSettings.SupportedResolutions)
        {
            _resolutionOption.AddItem(resolution.ToString());
            _resolutionOption.SetItemMetadata(
                _resolutionOption.ItemCount - 1,
                new Vector2I(resolution.Width, resolution.Height));
        }

        _uiScaleOption.Clear();
        foreach (int scale in ClientVisualSettings.SupportedUiScales)
        {
            AddOption(_uiScaleOption, $"{scale}%", scale);
        }
    }

    private void BindSettings(ClientVisualSettings settings)
    {
        ClientVisualSettings normalized = settings.Normalize();
        SelectMetadata(_windowModeOption, (int)normalized.WindowMode);
        SelectMetadata(
            _resolutionOption,
            new Vector2I(normalized.Resolution.Width, normalized.Resolution.Height));
        SelectMetadata(_uiScaleOption, normalized.UiScalePercent);
        _vsyncCheck.ButtonPressed = normalized.VSync;
        _reduceMotionCheck.ButtonPressed = normalized.ReduceMotion;
        RefreshResolutionAvailability();
    }

    private ClientVisualSettings ReadSettings()
    {
        var mode = (ClientWindowMode)_windowModeOption
            .GetItemMetadata(_windowModeOption.Selected)
            .AsInt32();
        Vector2I resolution = _resolutionOption
            .GetItemMetadata(_resolutionOption.Selected)
            .AsVector2I();
        int scale = _uiScaleOption
            .GetItemMetadata(_uiScaleOption.Selected)
            .AsInt32();
        return new ClientVisualSettings(
            mode,
            new ClientResolution(resolution.X, resolution.Y),
            scale,
            _vsyncCheck.ButtonPressed,
            _reduceMotionCheck.ButtonPressed);
    }

    private void RefreshResolutionAvailability()
    {
        ClientWindowMode mode = (ClientWindowMode)_windowModeOption
            .GetItemMetadata(_windowModeOption.Selected)
            .AsInt32();
        _resolutionOption.Disabled = mode == ClientWindowMode.BorderlessFullscreen;
        _resolutionOption.TooltipText = _resolutionOption.Disabled
            ? "无边框全屏使用当前显示器分辨率。"
            : "设置窗口客户区尺寸。";
    }

    private void OnStartPressed()
    {
        if (!_nativeAvailable || _busy)
        {
            return;
        }

        _errorLabel.Visible = false;
        SetBusy(true);
        StartRequested?.Invoke(new MatchSetup(
            ReadDeckKey(_player0Deck),
            ReadDeckKey(_player1Deck)));
    }

    private void ShowPage(MenuPage page)
    {
        bool changed = _page != page;
        _page = page;
        _pageTween?.Kill();
        _pageTween = null;
        _homePage.Modulate = Colors.White;
        _setupPage.Modulate = Colors.White;
        _settingsPage.Modulate = Colors.White;
        _errorPage.Modulate = Colors.White;
        _homePage.Visible = page == MenuPage.Home;
        _setupPage.Visible = page == MenuPage.Setup;
        _settingsPage.Visible = page == MenuPage.Settings;
        _errorPage.Visible = page == MenuPage.Error;

        Control target = page switch
        {
            MenuPage.Home => _homePage,
            MenuPage.Setup => _setupPage,
            MenuPage.Settings => _settingsPage,
            MenuPage.Error => _errorPage,
            _ => _homePage,
        };
        float duration = _automationRun || !changed
            ? 0.0f
            : ClientVisualSettingsRuntime.Duration(0.18f);
        if (duration > 0.0f)
        {
            target.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
            _pageTween = CreateTween()
                .SetEase(Tween.EaseType.Out)
                .SetTrans(Tween.TransitionType.Cubic);
            _pageTween.TweenProperty(target, "modulate", Colors.White, duration);
        }

        Control focusTarget = page switch
        {
            MenuPage.Home => _localHotseatButton,
            MenuPage.Setup => _player0Deck,
            MenuPage.Settings => _windowModeOption,
            MenuPage.Error => GetNode<Button>("%ErrorReturnButton"),
            _ => _localHotseatButton,
        };
        Callable.From(focusTarget.GrabFocus).CallDeferred();
    }

    private void UpdateAvailability(bool preserveMessage = false)
    {
        _localHotseatButton.Disabled = _busy || !_nativeAvailable;
        _startButton.Disabled = _busy || !_nativeAvailable;

        if (preserveMessage)
        {
            return;
        }

        if (_busy)
        {
            _availabilityLabel.Text = "● 正在创建安全热座会话";
            _availabilityLabel.Modulate = new Color(0.96f, 0.78f, 0.32f);
        }
        else if (_nativeAvailable)
        {
            _availabilityLabel.Text = "● 对战核心已就绪 · 本地热座可用";
            _availabilityLabel.Modulate = new Color(0.5f, 1.0f, 0.86f);
        }
        else
        {
            _availabilityLabel.Text = "● 正在检查对战核心";
            _availabilityLabel.Modulate = new Color(0.96f, 0.78f, 0.32f);
        }
    }

    private static void PopulateDecks(OptionButton selector, int defaultIndex)
    {
        selector.Clear();
        foreach ((string key, string label) in FixedDecks)
        {
            selector.AddItem(label);
            selector.SetItemMetadata(selector.ItemCount - 1, key);
        }

        selector.Select(defaultIndex);
    }

    private static void RefreshDeckPreview(
        OptionButton selector,
        Label faction,
        Label description)
    {
        string key = ReadDeckKey(selector);
        (string factionCopy, string descriptionCopy) = DeckCopy[key];
        faction.Text = factionCopy;
        description.Text = descriptionCopy;
    }

    private static string ReadDeckKey(OptionButton selector)
    {
        return selector.GetItemMetadata(selector.Selected).AsString();
    }

    private static void AddOption(OptionButton selector, string label, int metadata)
    {
        selector.AddItem(label);
        selector.SetItemMetadata(selector.ItemCount - 1, metadata);
    }

    private static void SelectMetadata(OptionButton selector, Variant expected)
    {
        for (int index = 0; index < selector.ItemCount; index++)
        {
            if (selector.GetItemMetadata(index).Equals(expected))
            {
                selector.Select(index);
                return;
            }
        }

        selector.Select(0);
    }

    private static bool IsAutomationRun(string[] arguments)
    {
        return arguments.Contains("--ci-smoke", StringComparer.Ordinal) ||
               arguments.Any(argument =>
                   argument.StartsWith("--ci-visual-suite=", StringComparison.Ordinal));
    }

    private enum MenuPage : byte
    {
        Home,
        Setup,
        Settings,
        Error,
    }
}
