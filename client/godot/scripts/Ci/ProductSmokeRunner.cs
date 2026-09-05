// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Text.Json;
using Godot;
using Scgs.GodotClient.Match;
using Scgs.GodotClient.Battlefield;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Ci;

internal sealed record ProductSmokeInput(Vector2? Position, Button? Button, Key? Key, int WindowId,
    BattlefieldSurfaceRef? Surface)
{
    internal bool Spatial => Surface.HasValue;
    internal static ProductSmokeInput ForButton(Button button) =>
        new(null, button, null, checked((int)button.GetWindow().GetWindowId()), null);
    internal static ProductSmokeInput Pointer(Vector2 position, BattlefieldSurfaceRef surface) => new(position, null, null, 0, surface);
    internal static ProductSmokeInput Keyboard(Key key, int windowId) => new(null, null, key, windowId, null);
}

internal sealed record ProductSmokeOptions(string ReportPath, string RunKind, string Coverage,
    string? CaptureDirectory = null, bool RequirePerformance = false)
{
    internal const string SuccessMarker = "SCGS_PRODUCT_V05_UI_SMOKE_OK";

    internal static void ConfigureViewport(Window window, IReadOnlyList<string> arguments)
    {
        string[] requested = arguments.Where(value => value.StartsWith("--ci-visual-viewport=", StringComparison.Ordinal)).ToArray();
        if (requested.Length > 1) throw new ArgumentException("Duplicate product viewport option.");
        string size = requested.Length == 0 ? "1600x900" : requested[0]["--ci-visual-viewport=".Length..];
        Vector2I pixels = size switch
        {
            "1280x720" => new(1280, 720), "1600x900" => new(1600, 900),
            "2560x1440" => new(2560, 1440), "2560x1600" => new(2560, 1600),
            _ => throw new ArgumentException("Unsupported product smoke viewport."),
        };
        // Headless ignores the executable's --resolution flag and otherwise
        // creates a 64x64 root window. Keep the same actual UI coordinate path.
        window.Size = pixels;
        window.ContentScaleSize = new Vector2I(1600, 900);
    }

    internal V05.GameConfigRequest CreateConfig(string player0Deck, string player1Deck, int matchIndex)
    {
        if (matchIndex < 0 || matchIndex >= 12) throw new ArgumentOutOfRangeException(nameof(matchIndex));
        // First preserve the short baseline. Subsequent real matches vary only
        // their reproducible shuffle/first-player config, never their decks.
        uint[] seeds = [0xC0DE_C0DEU, 17U, 17U, 17U, 311U, 733U, 1237U, 2027U, 4099U, 8191U, 12011U, 16001U];
        return new(player0Deck, player1Deck)
        {
            RandomSeed = seeds[matchIndex],
            FirstPlayerMode = matchIndex % 2 == 0 && matchIndex != 2 ? V05.FirstPlayerMode.Player0 : V05.FirstPlayerMode.Player1,
            ShuffleDecks = matchIndex != 0,
        };
    }

    internal static ProductSmokeOptions? Parse(IReadOnlyList<string> arguments)
    {
        bool enabled = arguments.Contains("--ci-product-smoke", StringComparer.Ordinal);
        string? Read(string prefix)
        {
            string[] values = arguments.Where(value => value.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            if (values.Length > 1) throw new ArgumentException("Duplicate product smoke option.");
            return values.Length == 0 ? null : values[0][prefix.Length..];
        }
        string? report = Read("--ci-product-report=");
        string? artifact = Read("--ci-product-artifact=");
        string? coverage = Read("--ci-product-coverage=");
        string? capture = Read("--ci-product-capture=");
        bool performance = arguments.Contains("--ci-product-performance", StringComparer.Ordinal);
        if (!enabled)
        {
            if (report is not null || artifact is not null || coverage is not null || capture is not null || performance)
                throw new ArgumentException("Product smoke options require --ci-product-smoke.");
            return null;
        }
        if (arguments.Any(value => value == "--ci-smoke" || value == "--legacy-2d-board" ||
            value.StartsWith("--ci-visual-suite=", StringComparison.Ordinal) ||
            value.StartsWith("--anime-", StringComparison.Ordinal)))
            throw new ArgumentException("Product and legacy/preview smoke modes cannot be combined.");
        if (string.IsNullOrWhiteSpace(report) || !Path.IsPathFullyQualified(report))
            throw new ArgumentException("An explicit absolute --ci-product-report path is required.");
        artifact ??= "source";
        coverage ??= artifact == "source" ? "full-ui" : "natural-ui";
        if (artifact is not ("source" or "export" or "zip") || coverage is not ("full-ui" or "natural-ui"))
            throw new ArgumentException("Invalid product smoke artifact or coverage mode.");
        if (capture is not null && !Path.IsPathFullyQualified(capture))
            throw new ArgumentException("Product capture directory must be absolute.");
        if (performance && capture is null)
            throw new ArgumentException("Product performance acceptance requires a capture directory.");
        return new(Path.GetFullPath(report), artifact, coverage,
            capture is null ? null : Path.GetFullPath(capture), performance);
    }
}

/// <summary>
/// Drives the actual ProductMatch through native Godot input. There is no
/// direct controller/native selection path. Missing action coverage is a
/// failure, never filled from legal-action enumeration or a v04 report.
/// </summary>
internal sealed class ProductSmokeRunner
{
    private readonly Node host;
    private readonly Func<ProductMatchScreen?> currentMatch;
    private readonly ProductSmokeOptions options;
    private readonly ProductVisualCapture? capture;
    private readonly List<ProductSmokeSession> sessions = [];
    private readonly HashSet<ProductSmokeSession> terminals = [];
    private readonly bool headless = DisplayServer.GetName() == "headless";
    private int pointerInputs;
    private int spatialInputs;
    private int keyboardInputs;
    private int inputSerial;
    private int invalidDragOwnerChecks;
    private int invalidDragZoneChecks;
    private int selectionBackChecks;
    private int naturalTerminals;
    private int surrenderTerminals;
    private bool used;

    internal ProductSmokeRunner(Node host, Func<ProductMatchScreen?> currentMatch, ProductSmokeOptions options,
        ProductVisualCapture? capture = null)
    {
        this.host = host;
        this.currentMatch = currentMatch;
        this.options = options;
        this.capture = capture;
        if (options.CaptureDirectory is not null && capture is null)
            throw new ArgumentException("Capture must be shared from the real menu/setup through the match.");
    }

    internal async Task<ProductSmokeReport> RunAsync()
    {
        try { return await RunCoreAsync(); }
        catch (Exception exception)
        {
            // Independent failure evidence has counters only. It must never
            // resemble a successful report or serialize private UI state.
            Directory.CreateDirectory(Path.GetDirectoryName(options.ReportPath)!);
            var failure = new
            {
                schema_version = 1, suite = "product-v05-ui-failure", success = false,
                reason = exception.GetType().Name, run_kind = options.RunKind,
                coverage = options.Coverage, action_counts = CountActions(),
                missing_actions = Enumerable.Range(0, 14).Where(kind => CountActions()[kind] == 0).ToArray(),
                completed_matches = terminals.Count, started_matches = sessions.Count,
                pointer_inputs = pointerInputs, spatial_inputs = spatialInputs, keyboard_inputs = keyboardInputs,
            };
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(options.ReportPath)!, "product-smoke-failure.json"),
                JsonSerializer.Serialize(failure, new JsonSerializerOptions { WriteIndented = true }));
            throw;
        }
    }

    private async Task<ProductSmokeReport> RunCoreAsync()
    {
        if (used) throw new InvalidOperationException("Product smoke runner cannot run twice.");
        used = true;
        var elapsed = Stopwatch.StartNew();
        bool surrender = false;
        string? profile = null;
        Vector2I physicalSize = host.GetWindow().Size;
        Vector2 size = new(physicalSize.X, physicalSize.Y);
        while (elapsed.Elapsed < TimeSpan.FromMinutes(8) && inputSerial < 16000)
        {
            await NextFrame();
            ProductMatchScreen? match = currentMatch();
            if (match is null || !GodotObject.IsInstanceValid(match) || !match.IsInsideTree()) continue;
            ProductSmokeSession audit = match.CiAudit;
            if (!sessions.Contains(audit))
            {
                if (!audit.IsRealNativeSession)
                    throw new InvalidOperationException("Product UI smoke requires the real v05 native session.");
                sessions.Add(audit);
                if (capture is not null) match.CiAttachProductVisual(capture);
            }
            profile ??= match.CiProductVisualProfile;
            if (profile != match.CiProductVisualProfile || profile != "anime-v1")
                throw new InvalidOperationException("Product UI smoke requires the default AnimeV1 presentation.");
            match.CiObserveSafeFrame();
            if (match.CiProductPrivacyVerificationPending) continue;
            await match.CiObserveProductPrivacyAsync();
            if (match.CiProductPrivacyVerificationPending) continue;
            CheckFailures();
            if (capture is not null)
            {
                await match.CiCaptureProductVisualAsync();
                await match.CiCaptureProductPerformanceAsync();
            }
            if (options.Coverage == "full-ui" && match.CiProductMode == ProductHotseatUiMode.Action)
            {
                if (selectionBackChecks == 0 && match.CiCanProbeStepBack)
                {
                    ProductSmokeUiStamp before = match.CiCurrentStamp;
                    ulong source = match.CiSelectedSource;
                    await Inject(ProductSmokeInput.Keyboard(Key.Escape, 0), audit);
                    match.CiAssertTargetStepBack(before, source);
                    ++selectionBackChecks;
                    continue;
                }
                bool wrongOwner = invalidDragOwnerChecks == 0;
                if ((wrongOwner || invalidDragZoneChecks == 0) && match.CiPlanInvalidDrag(wrongOwner) is { } probe)
                {
                    await InjectRejectedDrag(match, audit, probe);
                    if (wrongOwner) ++invalidDragOwnerChecks; else ++invalidDragZoneChecks;
                    continue;
                }
            }
            if (match.CiProductMode == ProductHotseatUiMode.Finished)
            {
                if (!terminals.Add(audit)) continue;
                if (audit.MatchEndedCount != 1 || !audit.MatchEndedLast || (uint)audit.Result == 0)
                    throw new InvalidOperationException("Product terminal event stream is incomplete or non-final.");
                if (audit.ActionCounts[10] != 0) ++surrenderTerminals;
                else ++naturalTerminals;
                int[] actions = CountActions();
                GD.Print($"SCGS_PRODUCT_UI_MATCH_COUNTS {sessions.Count}: {JsonSerializer.Serialize(actions)}");
                bool allNaturalActions = Enumerable.Range(0, 14).Where(kind => kind != 10)
                    .All(kind => actions[kind] > 0);
                bool complete = options.Coverage == "natural-ui" ||
                    allNaturalActions && sessions.Sum(session => session.ReactionSurrenders) > 0 &&
                        sessions.Sum(session => session.ChoiceSurrenders) > 0;
                if (complete)
                {
                    await Inject(match.CiReturnInput(), audit);
                    for (int frame = 0; frame < 180 && audit.DisposedCount == 0; ++frame) await NextFrame();
                    if (audit.DisposedCount != 1) throw new InvalidOperationException("Product menu return did not dispose its session.");
                    ProductSmokeReport report = BuildReport(profile, size);
                    ValidateCompleted(report);
                    capture?.Complete();
                    Directory.CreateDirectory(Path.GetDirectoryName(options.ReportPath)!);
                    await File.WriteAllTextAsync(options.ReportPath, JsonSerializer.Serialize(report,
                        new JsonSerializerOptions { WriteIndented = true }));
                    return report;
                }
                if (sessions.Count >= 12)
                    throw new InvalidOperationException("Product UI smoke could not cover all 14 actions in twelve real matches.");
                surrender = allNaturalActions;
                await Inject(match.CiRestartInput(), audit);
                continue;
            }
            ProductSmokeInput? next = match.CiNextUiInput(CountActions(), surrender, sessions.Count - 1,
                sessions.Sum(session => session.ChoiceSurrenders) == 0);
            if (next is not null) await Inject(next, audit);
        }
        throw new TimeoutException("Product UI smoke exceeded its bounded time/input budget.");
    }

    private async Task Inject(ProductSmokeInput operation, ProductSmokeSession audit)
    {
        audit.InputSerial = ++inputSerial;
        Key? key = operation.Key;
        // Exercise the same button through keyboard activation periodically.
        if (operation.Button is { } focused && !focused.Disabled && focused.IsVisibleInTree() &&
            focused.GetFocusModeWithOverride() != Control.FocusModeEnum.None &&
            (keyboardInputs == 0 || inputSerial % 7 == 0))
        {
            focused.GrabFocus();
            if (!focused.HasFocus())
                throw new InvalidOperationException("The visible product button did not accept keyboard focus.");
            key = Godot.Key.Enter;
        }
        if (key is { } actualKey)
        {
            ++keyboardInputs;
            Input.ParseInputEvent(new InputEventKey { Keycode = actualKey, PhysicalKeycode = actualKey,
                Pressed = true, WindowId = operation.WindowId });
            await NextFrame();
            Input.ParseInputEvent(new InputEventKey { Keycode = actualKey, PhysicalKeycode = actualKey,
                Pressed = false, WindowId = operation.WindowId });
        }
        else
        {
            ++pointerInputs;
            if (operation.Spatial) ++spatialInputs;
            Vector2 logicalPosition = operation.Button?.GetGlobalRect().GetCenter() ?? operation.Position ??
                throw new InvalidOperationException("Product smoke input has no live target.");
            Viewport viewport = operation.Button?.GetViewport() ?? host.GetViewport();
            Vector2 position = viewport.GetFinalTransform() * logicalPosition;
            Input.ParseInputEvent(new InputEventMouseMotion { Position = position, GlobalPosition = position,
                WindowId = operation.WindowId });
            await NextFrame();
            if (operation.Surface is { } surface)
            {
                ProductMatchScreen match = currentMatch() ?? throw new InvalidOperationException("Product UI vanished during pointer motion.");
                match.CiVerifyPointerTarget(logicalPosition, surface);
            }
            Input.ParseInputEvent(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true,
                WindowId = operation.WindowId });
            await NextFrame();
            Input.ParseInputEvent(new InputEventMouseButton { Position = position, GlobalPosition = position,
                ButtonIndex = MouseButton.Left, Pressed = false, WindowId = operation.WindowId });
        }
        await NextFrame();
    }

    private async Task InjectRejectedDrag(ProductMatchScreen match, ProductSmokeSession audit, ProductSmokeDragProbe probe)
    {
        audit.InputSerial = ++inputSerial;
        ++pointerInputs;
        ++spatialInputs;
        Vector2 fromLogical = probe.Source.Position!.Value;
        Vector2 toLogical = probe.Destination.Position!.Value;
        Transform2D transform = host.GetViewport().GetFinalTransform();
        Vector2 from = transform * fromLogical;
        Vector2 to = transform * toLogical;
        Input.ParseInputEvent(new InputEventMouseMotion { Position = from, GlobalPosition = from });
        await NextFrame();
        match.CiVerifyPointerTarget(fromLogical, probe.Source.Surface!.Value);
        Input.ParseInputEvent(new InputEventMouseButton { Position = from, GlobalPosition = from,
            ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true });
        await NextFrame();
        Input.ParseInputEvent(new InputEventMouseMotion { Position = to, GlobalPosition = to,
            Relative = to - from, ButtonMask = MouseButtonMask.Left });
        await NextFrame();
        if (!match.CiHasActiveDrag) throw new InvalidOperationException("Invalid-drop regression did not exercise a real drag token.");
        match.CiVerifyPointerTarget(toLogical, probe.Destination.Surface!.Value);
        Input.ParseInputEvent(new InputEventMouseButton { Position = to, GlobalPosition = to,
            ButtonIndex = MouseButton.Left, Pressed = false });
        await NextFrame();
        match.CiAssertNoSubmission(probe.Stamp, forbidNativeReads: true);
        for (int step = 0; step < 4 && match.CiHasSelection; ++step)
        {
            await Inject(ProductSmokeInput.Keyboard(Key.Escape, 0), audit);
            match.CiAssertNoSubmission(probe.Stamp);
        }
        if (match.CiHasSelection || match.CiHasActiveDrag)
            throw new InvalidOperationException("Esc did not clear the rejected drag selection/token.");
    }

    private async Task NextFrame()
    {
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (!headless) await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        ProductMatchScreen? match = currentMatch();
        if (match is not null && GodotObject.IsInstanceValid(match) && match.IsInsideTree()) match.CiObserveSafeFrame();
    }

    private int[] CountActions() => Enumerable.Range(0, 14).Select(index => sessions.Sum(session => session.ActionCounts[index])).ToArray();

    private void CheckFailures()
    {
        if (sessions.Any(session => session.PrematureViewReads != 0 || session.UnauthorizedPrivateQueries != 0 || session.PrivateLeaks != 0 ||
            session.UnattributedCommands != 0 || session.EngineFailures != 0))
            throw new InvalidOperationException("Product smoke observed a privacy, attribution or engine failure.");
    }

    private ProductSmokeReport BuildReport(string profile, Vector2 viewport)
    {
        ProductSmokeSession last = sessions[^1];
        return new ProductSmokeReport
        {
            VisualProfile = profile, RunKind = options.RunKind, Coverage = options.Coverage,
            FrameClock = headless ? "process-frame" : "frame-post-draw",
            ViewportWidth = (int)viewport.X, ViewportHeight = (int)viewport.Y,
            PointerInputs = pointerInputs, SpatialInputs = spatialInputs, KeyboardInputs = keyboardInputs,
            InvalidDragOwnerChecks = invalidDragOwnerChecks, InvalidDragZoneChecks = invalidDragZoneChecks,
            SelectionBackChecks = selectionBackChecks,
            ReactionSurrenderChecks = sessions.Sum(session => session.ReactionSurrenders),
            ChoiceSurrenderChecks = sessions.Sum(session => session.ChoiceSurrenders),
            Commands = sessions.Sum(session => session.Commands), ActionCounts = CountActions(),
            NaturalTerminals = naturalTerminals, SurrenderTerminals = surrenderTerminals,
            Restarts = sessions.Count - 1, DisposedSessions = sessions.Sum(session => session.DisposedCount),
            CoveredSamples = sessions.Sum(session => session.CoveredSamples),
            ResolvingSamples = sessions.Sum(session => session.ResolvingSamples),
            MinimumPublicFrames = sessions.Min(session => session.MinimumPublicFrames),
            PrematureViewReads = sessions.Sum(session => session.PrematureViewReads),
            UnauthorizedPrivateQueries = sessions.Sum(session => session.UnauthorizedPrivateQueries),
            SchedulingQueries = sessions.Sum(session => session.SchedulingQueries),
            PrivateStateLeaks = sessions.Sum(session => session.PrivateLeaks),
            UnattributedCommands = sessions.Sum(session => session.UnattributedCommands),
            EngineFailures = sessions.Sum(session => session.EngineFailures),
            TerminalEventChecks = terminals.Count, TerminalResult = (int)last.Result,
            FinalRevision = last.Revision, Success = true,
        };
    }

    private static void ValidateCompleted(ProductSmokeReport report)
    {
        int[] required = report.Coverage == "full-ui" ? Enumerable.Range(0, 14).ToArray() : [0, 1, 2, 4, 9];
        if (required.Any(kind => report.ActionCounts[kind] == 0) || report.NaturalTerminals < 1 ||
            report.MinimumPublicFrames < 2 || report.SpatialInputs < 1 || report.KeyboardInputs < 1 ||
            report.CoveredSamples < 2 || report.ResolvingSamples < 2 ||
            report.DisposedSessions != report.TerminalEventChecks ||
            report.PrematureViewReads != 0 || report.UnauthorizedPrivateQueries != 0 || report.PrivateStateLeaks != 0 ||
            report.UnattributedCommands != 0 || report.EngineFailures != 0 ||
            (report.Coverage == "full-ui" && (report.SurrenderTerminals < 1 || report.Restarts < 1 ||
                report.InvalidDragOwnerChecks < 1 || report.InvalidDragZoneChecks < 1 || report.SelectionBackChecks < 1 ||
                report.ReactionSurrenderChecks < 1 || report.ChoiceSurrenderChecks < 1)))
            throw new InvalidOperationException("Product UI evidence is incomplete; no success report may be emitted.");
    }
}
