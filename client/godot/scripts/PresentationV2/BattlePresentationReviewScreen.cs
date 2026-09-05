// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Scgs.GodotClient.Match;
using Scgs.GodotClient.Native;
using Scgs.GodotClient.Visuals;
using Scgs.Hotseat.ProductReview;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>Explicit, real-rule review entry. Never reveals or plays on behalf of its human viewer.</summary>
public sealed partial class BattlePresentationReviewScreen : Control
{
    private const string BaseCommit = "f0602683ea7cd37e2e327a9f389d5f2193c14c02";
    private string sourceSha = BaseCommit;
    private bool suppliedSha;
    private Control menu = null!;
    private Control matchHost = null!;
    private Control toolbar = null!;
    private Label status = null!;
    private ProductMatchScreen? match;
    private V05.IScgsV05GameSession? session;
    private PresentationReviewKind? currentKind;
    private bool busy;
    private bool leaving;

    public event Action? ExitRequested;

    public void ConfigureArguments(IReadOnlyList<string> arguments)
    {
        string[] values = arguments.Where(value => value.StartsWith("--review-source-sha=", StringComparison.Ordinal)).ToArray();
        if (values.Length > 1) throw new ArgumentException("Only one --review-source-sha may be supplied.");
        if (values.Length == 0) return;
        string value = values[0]["--review-source-sha=".Length..];
        if (value.Length != 40 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("--review-source-sha requires a full 40-character commit hash.");
        sourceSha = value.ToLowerInvariant();
        suppliedSha = true;
    }

    public override void _Ready()
    {
        BattlePresentationReviewRuntime.Configure(true);
        menu = GetNode<Control>("%ReviewMenu");
        matchHost = GetNode<Control>("%ReviewMatchHost");
        toolbar = GetNode<Control>("%ReviewToolbar");
        status = GetNode<Label>("%ReviewStatus");
        GetNode<Button>("%ReviewOathguardButton").Pressed += () => RequestScene(PresentationReviewKind.Oathguard);
        GetNode<Button>("%ReviewPactmageButton").Pressed += () => RequestScene(PresentationReviewKind.Pactmage);
        GetNode<Button>("%ReviewSpellButton").Pressed += () => RequestScene(PresentationReviewKind.Spell);
        GetNode<Button>("%ReviewBackButton").Pressed += RequestBack;
        GetNode<Button>("%ReviewRestartButton").Pressed += Restart;
        GetNode<Button>("%ReviewExitButton").Pressed += Exit;
        toolbar.Hide();
        status.Text = "选择场面后，请主动揭示手牌，再亲手出牌或进化。";
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (match is not null || !@event.IsActionPressed("ui_cancel")) return;
        GetViewport().SetInputAsHandled();
        if (!busy) Exit();
    }

    public override void _ExitTree()
    {
        leaving = true;
        StopMatch();
        BattlePresentationReviewRuntime.Configure(false);
    }

    private void RequestScene(PresentationReviewKind kind)
    {
        if (busy || leaving) return;
        busy = true;
        match?.PrepareForSceneExit();
        status.Text = "正在通过真实规则准备场面……";
        SetButtonsDisabled(true);
        Callable.From(() => PrepareScene(kind)).CallDeferred();
    }

    private void PrepareScene(PresentationReviewKind kind)
    {
        if (leaving || !IsInsideTree()) return;
        try
        {
            StopMatch();
            menu.Show();
            toolbar.Hide();
            string native = NativeLibraryLocator.ResolveProductAbsolutePath();
            using PreparedPresentationReview prepared = PresentationReviewScenario.Prepare(
                kind, config => V05.ScgsV05GameSession.Create(config, native), sourceSha);
            if (!PresentationReviewScenario.ValidateTrace(prepared))
                throw new InvalidOperationException("The legal preparation trace did not validate.");
            SaveTrace(prepared);
            ProductMatchScreen product = GD.Load<PackedScene>("res://scenes/match/ProductMatch.tscn")
                .Instantiate<ProductMatchScreen>();
            session = prepared.TakeSession();
            match = product;
            currentKind = kind;
            product.ExitRequested += RequestBack;
            product.RestartRequested += Restart;
            matchHost.AddChild(product);
            product.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            product.Begin(session, MatchVisualIdentity.FromDecks(
                prepared.Config.Player0Deck, prepared.Config.Player1Deck), enablePresentation: true);
            // Begin only constructs a Covered controller. No Reveal, GetView,
            // selection, command or presentation completion is issued here.
            menu.Hide();
            toolbar.Show();
        }
        catch (Exception exception)
        {
            StopMatch();
            menu.Show();
            toolbar.Hide();
            status.Text = $"场面准备失败：{exception.Message}\n可以重试或退出验收。";
        }
        finally
        {
            busy = false;
            SetButtonsDisabled(false);
        }
    }

    private void SaveTrace(PreparedPresentationReview prepared)
    {
        string directory = ProjectSettings.GlobalizePath("user://review-evidence");
        Directory.CreateDirectory(directory);
        string filename = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{prepared.Kind.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.json";
        using FileStream output = new(Path.Combine(directory, filename), FileMode.CreateNew, System.IO.FileAccess.Write);
        JsonSerializer.Serialize(output, new
        {
            schema_version = 1,
            suite = "real-product-battle-presentation-review",
            development_private_evidence = true,
            source_sha = sourceSha,
            source_provenance = suppliedSha ? "operator-supplied-not-working-tree-attested" : "base-commit-plus-working-tree",
            working_tree_not_attested = true,
            utc = DateTime.UtcNow,
            prepared.Kind,
            prepared.Config,
            prepared.InitialSha256,
            prepared.Trace,
            prepared.TraceSha256,
            ready_command = prepared.ReadyAction.Command,
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = true,
        });
    }

    private void Restart()
    {
        if (currentKind.HasValue) RequestScene(currentKind.Value);
    }

    private void RequestBack()
    {
        if (busy || leaving) return;
        busy = true;
        match?.PrepareForSceneExit();
        Callable.From(() =>
        {
            if (leaving || !IsInsideTree()) return;
            StopMatch();
            toolbar.Hide();
            menu.Show();
            status.Text = "选择另一个场面；每次准备都会创建新的真实对局。";
            busy = false;
        }).CallDeferred();
    }

    private void StopMatch()
    {
        if (match is { } previous && GodotObject.IsInstanceValid(previous))
        {
            previous.ExitRequested -= RequestBack;
            previous.RestartRequested -= Restart;
            previous.PrepareForSceneExit();
            previous.ProcessMode = ProcessModeEnum.Disabled;
            if (previous.GetParent() is { } parent) parent.RemoveChild(previous);
            previous.QueueFree();
        }
        match = null;
        session?.Dispose();
        session = null;
    }

    private void SetButtonsDisabled(bool disabled)
    {
        foreach (string name in new[] { "ReviewOathguardButton", "ReviewPactmageButton", "ReviewSpellButton",
                     "ReviewBackButton", "ReviewRestartButton", "ReviewExitButton" })
            GetNode<Button>("%" + name).Disabled = disabled;
    }

    private void Exit()
    {
        if (busy || leaving) return;
        leaving = true;
        StopMatch();
        BattlePresentationReviewRuntime.Configure(false);
        ExitRequested?.Invoke();
    }
}
