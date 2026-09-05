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
    private const string CardFrameBaseCommit = "a7eb363ba0b3790cced6dc03646bbd8ca2c2aa0c";
    private string sourceSha = BaseCommit;
    private bool suppliedSha;
    private bool explicitlyConfiguredEntry;
    private ProductReviewLaunchOptions reviewOptions = ProductReviewLaunchOptions.Parse(
        new[] { "--battle-presentation-review" })!;
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

    // The inherited CardFrameReview.tscn can be run with Editor F6/MCP without
    // requiring command-line arguments. The original scene keeps false.
    [Export]
    public bool CardFrameReview { get; set; }

    /// <summary>
    /// Read-only layout fixture catalogue, not rendered evidence and never a
    /// mutation of the prepared native match. Each consumer must preserve the
    /// explicit synthetic label in any separate visual fixture it constructs.
    /// </summary>
    public string ReviewDescribeSyntheticSamples() => JsonSerializer.Serialize(new
    {
        enabled = CardFrameReviewRuntime.Enabled,
        synthetic = true,
        rendered = false,
        native_session_accessed = false,
        evidence_kind = "synthetic-card-frame-layout-catalogue-not-gpu-evidence",
        samples = CardFrameReviewRuntime.Enabled ? CardFrameSyntheticSamples.All : [],
    }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

    public void ConfigureArguments(IReadOnlyList<string> arguments)
    {
        ProductReviewLaunchOptions? parsed = ProductReviewLaunchOptions.Parse(arguments);
        explicitlyConfiguredEntry = parsed is not null;
        reviewOptions = parsed ??
            ProductReviewLaunchOptions.Parse(arguments.Append("--battle-presentation-review").ToArray())!;
        sourceSha = reviewOptions.SourceSha ?? BaseCommit;
        suppliedSha = reviewOptions.SourceSha is not null;
    }

    public override void _Ready()
    {
        if (CardFrameReview)
        {
            if (explicitlyConfiguredEntry && !reviewOptions.EnableCardFrame)
                throw new ArgumentException("The card-frame scene cannot also enable the battle-presentation entry.");
            reviewOptions = ProductReviewLaunchOptions.Parse(new[] { "--card-frame-review" })!;
        }
        BattlePresentationReviewRuntime.Configure(reviewOptions.EnableBattlePresentation);
        CardFrameReviewRuntime.Configure(reviewOptions.EnableCardFrame);
        if (!suppliedSha && reviewOptions.EnableCardFrame) sourceSha = CardFrameBaseCommit;
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
        if (reviewOptions.EnableCardFrame)
        {
            const string optionsPath = "ReviewMenu/Panel/Margin/Options/";
            GetNode<Label>(optionsPath + "Title").Text = "卡框精修 R1 · 独立实机审阅";
            GetNode<Label>(optionsPath + "Description").Text =
                "真实 v05 对局、合法命令准备，保留主动揭示与正常操作。\n本入口只审阅三张卡的卡框与可读性，不代表新战斗演出完成。";
            GetNode<Button>("%ReviewOathguardButton").Text = "曜誓大团长 · 卡框与进化异画";
            GetNode<Button>("%ReviewPactmageButton").Text = "禁忌毕业生 · 卡框与进化异画";
            GetNode<Button>("%ReviewSpellButton").Text = "界域裁定 · 法术卡框";
            GetNode<Button>("%ReviewBackButton").Text = "返回卡框审阅";
            GetNode<Button>("%ReviewExitButton").Text = "退出卡框审阅";
            status.Text = "待审阅候选，尚未获视觉批准。请选择场面，再主动揭示；沿用上一轮基础动作，本轮只审卡框。";
        }
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
        CardFrameReviewRuntime.Configure(false);
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
                prepared.Config.Player0Deck, prepared.Config.Player1Deck),
                enablePresentation: reviewOptions.EnablePresentationPlayback);
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
        string lane = reviewOptions.EnableCardFrame ? "card-frame-" : string.Empty;
        string filename = $"{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{lane}{prepared.Kind.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}.json";
        using FileStream output = new(Path.Combine(directory, filename), FileMode.CreateNew, System.IO.FileAccess.Write);
        JsonSerializer.Serialize(output, new
        {
            schema_version = 1,
            suite = reviewOptions.EvidenceSuite,
            review_entry = reviewOptions.EnableCardFrame ? "card-frame-review" : "battle-presentation-review",
            synthetic = false,
            candidate_battle_presentation_enabled = reviewOptions.EnableBattlePresentation,
            presentation_playback_enabled = reviewOptions.EnablePresentationPlayback,
            presentation_effects_revision = "battle-presentation-v2-stage1-unchanged",
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
        CardFrameReviewRuntime.Configure(false);
        ExitRequested?.Invoke();
    }
}
