// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Preview;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Scgs.GodotClient.Ci;

internal sealed class AnimeCardBodySliceSuite
{
    private const int MaximumStableFramePairAttempts = 30;
    private const int MaximumViewportConvergenceAttempts = 8;
    private readonly AnimeCardBodySliceScreen _screen;
    private readonly string _outputDirectory;
    private readonly AnimeSliceViewportSize? _captureViewport;

    internal AnimeCardBodySliceSuite(AnimeCardBodySliceScreen screen, string outputDirectory)
    {
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
        if (string.Equals(DisplayServer.GetName(), "headless", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The AnimeV1 card-body suite requires a display-backed Compatibility renderer.");
        }
        if (!Path.IsPathFullyQualified(outputDirectory))
        {
            throw new ArgumentException("Card-body output must be absolute.", nameof(outputDirectory));
        }
        _outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(_outputDirectory);
        _captureViewport = AnimeVisualSliceViewportPolicy.Resolve(OS.GetCmdlineUserArgs());
        if (_captureViewport is { } viewport)
        {
            // Capture the requested client area rather than an outer decorated
            // window. This matters at 2560x1600, where a Windows title bar
            // otherwise reduces the captured viewport to 1570 px high. The
            // product minimum is 1280x720, but the hosted macOS display is the
            // explicitly isolated 1024x684 CI exception; release that minimum
            // before sizing so AppKit does not clamp the drawable height.
            DisplayServer.WindowSetMinSize(Vector2I.Zero);
            DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
            DisplayServer.WindowSetPosition(Vector2I.Zero);
            DisplayServer.WindowSetSize(new Vector2I(viewport.Width, viewport.Height));
        }
    }

    internal async Task<string> RunAsync()
    {
        await EnsureExactCaptureViewportAsync();
        var captures = new List<AnimeCardBodyCapture>();
        int sequence = 0;
        foreach (string state in AnimeCardBodySliceLaunch.States)
        {
            _screen.SetPreviewState(state);
            (Image stableImage, AnimeCardBodyFrameStabilityEvidence stability) =
                await ReadStableFramePairAsync(state);
            using Image image = stableImage;
            AnimeCardBodySliceEvidence evidence = _screen.MeasureEvidence();
            AnimeCardBodyGpuReadabilityEvidence gpuReadability;
            if (AnimeCardBodyGpuReadabilityPolicy.RequiresEvidence(state))
            {
                var actorValueReferences = new Dictionary<string, Image>(StringComparer.Ordinal);
                var actorNameReferences = new Dictionary<string, Image>(StringComparer.Ordinal);
                try
                {
                    foreach (string actorName in _screen.GetGpuReferenceActorNames())
                    {
                        _screen.SetGpuValueLabelsVisibleForActor(actorName, false);
                        try
                        {
                            actorValueReferences.Add(actorName, await ReadSettledFrameAsync());
                        }
                        finally
                        {
                            _screen.SetGpuValueLabelsVisibleForActor(actorName, true);
                        }

                        _screen.SetGpuNameLabelVisibleForActor(actorName, false);
                        try
                        {
                            actorNameReferences.Add(actorName, await ReadSettledFrameAsync());
                        }
                        finally
                        {
                            _screen.SetGpuNameLabelVisibleForActor(actorName, true);
                        }
                    }
                    gpuReadability = _screen.MeasureGpuReadability(
                        image,
                        actorValueReferences,
                        actorNameReferences);
                }
                finally
                {
                    _screen.SetGpuValueLabelsVisible(true);
                    _screen.SetGpuNameLabelsVisible(true);
                    foreach (Image reference in actorValueReferences.Values)
                    {
                        reference.Dispose();
                    }
                    foreach (Image reference in actorNameReferences.Values)
                    {
                        reference.Dispose();
                    }
                }
            }
            else
            {
                gpuReadability = _screen.MeasureGpuReadability(
                    image,
                    new Dictionary<string, Image>(StringComparer.Ordinal),
                    new Dictionary<string, Image>(StringComparer.Ordinal));
            }
            AnimeCardBodySilhouetteEvidence silhouetteIsolation;
            if (AnimeCardBodySilhouettePolicy.RequiresEvidence(state))
            {
                var actorReferences = new Dictionary<string, Image>(StringComparer.Ordinal);
                try
                {
                    foreach (string actorName in _screen.GetSilhouetteReferenceActorNames())
                    {
                        _screen.SetProductFaceLayersVisibleForActor(actorName, false);
                        try
                        {
                            actorReferences.Add(actorName, await ReadSettledFrameAsync());
                        }
                        finally
                        {
                            _screen.SetProductFaceLayersVisibleForActor(actorName, true);
                        }
                    }
                    silhouetteIsolation = _screen.MeasureSilhouetteIsolation(image, actorReferences);
                }
                finally
                {
                    _screen.SetProductFaceLayersVisible(true);
                    foreach (Image reference in actorReferences.Values)
                    {
                        reference.Dispose();
                    }
                }
            }
            else
            {
                silhouetteIsolation = _screen.MeasureSilhouetteIsolation(
                    image,
                    new Dictionary<string, Image>(StringComparer.Ordinal));
            }
            string file = $"{sequence++:00}-{state}.png";
            string path = Path.Combine(_outputDirectory, file);
            Error error = image.SavePng(path);
            if (error != Error.Ok)
            {
                throw new IOException($"Could not save card-body state {state} ({error}).");
            }
            // Persist the actual frame before contract validation so an
            // always-upload CI artifact explains any visual failure.
            Validate(state, evidence, gpuReadability, silhouetteIsolation);
            captures.Add(new AnimeCardBodyCapture
            {
                State = state,
                File = file,
                Width = image.GetWidth(),
                Height = image.GetHeight(),
                Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                FrameStability = stability,
                Evidence = evidence,
                GpuReadability = gpuReadability,
                SilhouetteIsolation = silhouetteIsolation,
            });
            GD.Print($"SCGS_ANIME_CARD_BODY_CAPTURE_OK state={state} path={path}");
        }

        var report = new AnimeCardBodySliceReport
        {
            Captures = captures,
        };
        string reportPath = Path.Combine(_outputDirectory, "anime-card-body-slice.json");
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, json + System.Environment.NewLine, new UTF8Encoding(false));
        return reportPath;
    }

    private async Task EnsureExactCaptureViewportAsync()
    {
        if (_captureViewport is not { } target)
        {
            return;
        }

        AnimeSliceViewportSize requestedWindow = target;
        string lastObservation = "no framebuffer sampled";
        for (int attempt = 1; attempt <= MaximumViewportConvergenceAttempts; attempt++)
        {
            using Image firstFrame = await ReadCompletedFrameAsync();
            using Image secondFrame = await ReadCompletedFrameAsync();
            var firstObserved = new AnimeSliceViewportSize(
                firstFrame.GetWidth(),
                firstFrame.GetHeight());
            var observed = new AnimeSliceViewportSize(
                secondFrame.GetWidth(),
                secondFrame.GetHeight());
            Vector2I actualWindow = DisplayServer.WindowGetSize();
            lastObservation =
                $"attempt={attempt}, requested-window={requestedWindow.Width}x{requestedWindow.Height}, " +
                $"actual-window={actualWindow.X}x{actualWindow.Y}, " +
                $"framebuffers={firstObserved.Width}x{firstObserved.Height}," +
                $"{observed.Width}x{observed.Height}";
            if (firstObserved != observed)
            {
                continue;
            }
            if (observed == target)
            {
                GD.Print(
                    $"SCGS_ANIME_CARD_BODY_VIEWPORT_OK target={target.Width}x{target.Height} " +
                    $"window={actualWindow.X}x{actualWindow.Y} attempts={attempt}");
                return;
            }

            requestedWindow =
                AnimeVisualSliceViewportPolicy.CorrectWindowSizeForFramebuffer(
                    requestedWindow,
                    observed,
                    target);
            DisplayServer.WindowSetPosition(Vector2I.Zero);
            DisplayServer.WindowSetSize(
                new Vector2I(requestedWindow.Width, requestedWindow.Height));
        }

        throw new InvalidOperationException(
            $"AnimeV1 card-body capture could not converge to exact framebuffer " +
            $"{target.Width}x{target.Height} within {MaximumViewportConvergenceAttempts} " +
            $"complete frames; last observation: {lastObservation}.");
    }

    private static void Validate(
        string state,
        AnimeCardBodySliceEvidence evidence,
        AnimeCardBodyGpuReadabilityEvidence gpuReadability,
        AnimeCardBodySilhouetteEvidence silhouetteIsolation)
    {
        if (evidence.ActorCount == 0 ||
            evidence.ActorCount != evidence.IntegratedActorCount ||
            evidence.SubViewportCount != 0 ||
            evidence.UsesNativeSession)
        {
            throw new InvalidOperationException(
                $"Card-body integration evidence failed for {state}: " +
                $"actors={evidence.ActorCount}, integrated={evidence.IntegratedActorCount}, " +
                $"subviewports={evidence.SubViewportCount}, native={evidence.UsesNativeSession}.");
        }
        if (state == AnimeCardBodySliceLaunch.StateContact &&
            (evidence.ActorCount != 60 || evidence.DistinctStyleCount != 60))
        {
            throw new InvalidOperationException(
                "The contact sheet must cover exactly 5 kinds × 3 factions × 4 rarities.");
        }
        if (state == AnimeCardBodySliceLaunch.StateRepresentatives && evidence.ActorCount != 9)
        {
            throw new InvalidOperationException("The representative state must contain seven base cards and two evolved cards.");
        }
        if (state == AnimeCardBodySliceLaunch.StateContexts &&
            !new[] { "Detail", "Field", "Hand" }.All(evidence.Contexts.Contains))
        {
            throw new InvalidOperationException("The context state must exercise detail, hand and field composition.");
        }
        if (gpuReadability.State != state ||
            gpuReadability.Required != AnimeCardBodyGpuReadabilityPolicy.RequiresEvidence(state) ||
            !AnimeCardBodyGpuReadabilityPolicy.IsCaptureReadable(gpuReadability))
        {
            string failures = string.Join(
                "; ",
                gpuReadability.Actors.SelectMany(actor => actor.Badges
                    .Where(badge =>
                        !badge.Readable ||
                        !string.Equals(
                            badge.ReferenceActorName,
                            actor.ActorName,
                            StringComparison.Ordinal) ||
                        !AnimeCardBodyGpuReadabilityPolicy.IsBadgeReadable(
                            badge,
                            gpuReadability.MinimumBadgePixelHeight))
                    .Select(badge =>
                        $"{actor.ActorName}/{badge.Role}: h={badge.PixelHeight}, " +
                        $"glyph={badge.GlyphPixelWidth}x{badge.GlyphPixelHeight}, " +
                        $"inside={badge.FullyInsideViewport}, bright={badge.BrightPixelCount}, " +
                        $"diff={badge.GlyphDifferencePixelCount}, " +
                        $"bright-diff={badge.BrightGlyphDifferencePixelCount}, " +
                        $"high-contrast={badge.HighContrastGlyphDifferencePixelCount}, " +
                        $"max-contrast={badge.MaximumGlyphContrast:F3}, " +
                        $"socket-insets=({badge.GlyphSocketInsetLeft:F1}," +
                        $"{badge.GlyphSocketInsetTop:F1},{badge.GlyphSocketInsetRight:F1}," +
                        $"{badge.GlyphSocketInsetBottom:F1})")));
            string nameFailures = string.Join(
                "; ",
                gpuReadability.Actors
                    .Where(actor =>
                        !actor.NameReadable ||
                        !string.Equals(
                            actor.Name.ReferenceActorName,
                            actor.ActorName,
                            StringComparison.Ordinal) ||
                        !AnimeCardBodyGpuReadabilityPolicy.IsNameReadable(
                            actor.Name,
                            gpuReadability.ViewportWidth,
                            gpuReadability.ViewportHeight))
                    .Select(actor =>
                        $"{actor.ActorName}/name '{actor.Name.Text}': " +
                        $"source='{actor.Name.SourceText}', full={actor.Name.FullNameMatchesSource}, " +
                        $"font={actor.Name.FontSize}, " +
                        $"glyph={actor.Name.GlyphPixelWidth}x{actor.Name.GlyphPixelHeight}, " +
                        $"diff={actor.Name.GlyphDifferencePixelCount}, " +
                        $"high-contrast={actor.Name.HighContrastGlyphDifferencePixelCount}, " +
                        $"glyph-insets=({actor.Name.GlyphSocketInsetLeft:F1}," +
                        $"{actor.Name.GlyphSocketInsetTop:F1}," +
                        $"{actor.Name.GlyphSocketInsetRight:F1}," +
                        $"{actor.Name.GlyphSocketInsetBottom:F1}), " +
                        $"socket-plate-insets=({actor.Name.TextSocketNamePlateInsetLeft:F1}," +
                        $"{actor.Name.TextSocketNamePlateInsetTop:F1}," +
                        $"{actor.Name.TextSocketNamePlateInsetRight:F1}," +
                        $"{actor.Name.TextSocketNamePlateInsetBottom:F1}), " +
                        $"required-horizontal-inset=" +
                        $"{actor.Name.RequiredNamePlateHorizontalInsetPixels:F1}, " +
                        $"center-delta=({actor.Name.GlyphSocketCenterDeltaX:F2}," +
                        $"{actor.Name.GlyphSocketCenterDeltaY:F2})/" +
                        $"{actor.Name.MaximumGlyphSocketCenterDeltaPixels:F2}"));
            string structuralFailures =
                $"actors={gpuReadability.ActorCount}/{gpuReadability.Actors.Count}, " +
                $"badges={gpuReadability.Actors.Sum(actor => actor.Badges.Count)}/" +
                $"{gpuReadability.RequiredBadgeCount}, " +
                $"names={gpuReadability.RequiredNameCount}/{gpuReadability.ActorCount}, " +
                $"complete-names={gpuReadability.CompleteNameCount}/" +
                $"{gpuReadability.ActorCount}, " +
                $"local-failures={gpuReadability.Actors.Count(actor => !actor.LocalCompositionReadable)}, " +
                $"badges-readable={gpuReadability.AllRequiredBadgesReadable}, " +
                $"names-readable={gpuReadability.AllRequiredNamesReadable}";
            throw new InvalidOperationException(
                $"Final-screen GPU text readability failed for {state}: " +
                $"{string.Join("; ", new[] { failures, nameFailures, structuralFailures }.Where(value => !string.IsNullOrEmpty(value)))}.");
        }
        if (silhouetteIsolation.State != state ||
            silhouetteIsolation.Required != AnimeCardBodySilhouettePolicy.RequiresEvidence(state) ||
            !AnimeCardBodySilhouettePolicy.IsCaptureIsolated(silhouetteIsolation))
        {
            string detail = string.Join(
                "; ",
                silhouetteIsolation.Probes
                    .Where(probe => !probe.Passed)
                    .Select(probe =>
                        $"{probe.ActorName}/{probe.Corner}: " +
                        $"inside={probe.FullyInsideViewport}, " +
                        $"delta={probe.CornerBackgroundColorDelta:F3}"));
            string interiorDetail = string.Join(
                "; ",
                silhouetteIsolation.InteriorProbes
                    .Where(probe => !probe.Passed)
                    .Select(probe =>
                        $"{probe.ActorName}/interior: " +
                        $"inside={probe.FullyInsideViewport}, " +
                        $"at=({probe.ScreenX:F1},{probe.ScreenY:F1}), " +
                        $"diff={probe.ProductLayerDifferencePixelCount}"));
            string combinedDetail = string.Join(
                "; ",
                new[] { detail, interiorDetail }
                    .Where(value => !string.IsNullOrEmpty(value)));
            throw new InvalidOperationException(
                $"Final-screen silhouette isolation failed for {state}: " +
                (string.IsNullOrEmpty(combinedDetail)
                    ? $"structural product layer evidence failed " +
                      $"(bases-hidden={silhouetteIsolation.AllRectangularBasesHidden})"
                    : combinedDetail));
        }
    }

    private async Task<Image> ReadCompletedFrameAsync()
    {
        await _screen.ToSignal(_screen.GetTree(), SceneTree.SignalName.ProcessFrame);
        await _screen.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image viewportImage = _screen.GetViewport().GetTexture().GetImage();
        // Some Compatibility backends recycle the readback Image storage on a
        // later viewport capture. Own the bytes so every per-actor reference
        // remains the exact frame observed while that actor alone was hidden.
        return Image.CreateFromData(
            viewportImage.GetWidth(),
            viewportImage.GetHeight(),
            viewportImage.HasMipmaps(),
            viewportImage.GetFormat(),
            viewportImage.GetData());
    }

    private async Task<Image> ReadSettledFrameAsync()
    {
        // Compatibility rendering may expose the frame submitted immediately
        // before a visibility mutation. Consume that transition frame, then
        // retain the next completed GPU frame as the isolated reference.
        using Image transition = await ReadCompletedFrameAsync();
        return await ReadCompletedFrameAsync();
    }

    private async Task<(Image Image, AnimeCardBodyFrameStabilityEvidence Evidence)>
        ReadStableFramePairAsync(string state)
    {
        Image previous = await ReadCompletedFrameAsync();
        AnimeCardBodyFrameSample previousSample;
        try
        {
            previousSample = Sample(previous);
        }
        catch
        {
            previous.Dispose();
            throw;
        }

        string lastDifference = "no adjacent frame pair was sampled";
        try
        {
            for (int attempt = 1; attempt <= MaximumStableFramePairAttempts; attempt++)
            {
                Image? current = null;
                try
                {
                    current = await ReadCompletedFrameAsync();
                    AnimeCardBodyFrameSample currentSample = Sample(current);
                    if (previousSample.HasIdenticalPixels(currentSample))
                    {
                        AnimeCardBodyFrameFingerprint previousFingerprint = previousSample.Fingerprint;
                        AnimeCardBodyFrameFingerprint currentFingerprint = currentSample.Fingerprint;
                        var evidence = new AnimeCardBodyFrameStabilityEvidence
                        {
                            ConsecutiveFramePostDraws = 2,
                            AttemptCount = attempt,
                            PixelFormat = currentFingerprint.PixelFormat,
                            PixelByteLength = currentFingerprint.PixelByteLength,
                            FirstPixelSha256 = previousFingerprint.PixelSha256,
                            SecondPixelSha256 = currentFingerprint.PixelSha256,
                        };
                        Image stable = current;
                        current = null;
                        return (stable, evidence);
                    }

                    lastDifference = previousSample.Fingerprint.DescribeDifference(currentSample.Fingerprint);
                    previous.Dispose();
                    previous = current;
                    previousSample = currentSample;
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
            $"AnimeV1 card-body state {state} did not produce two pixel-identical consecutive " +
            $"FramePostDraws within {MaximumStableFramePairAttempts} attempts; last mismatch: " +
            $"{lastDifference}.");
    }

    private static AnimeCardBodyFrameSample Sample(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        byte[] pixels = image.GetData();
        return AnimeCardBodyFrameSample.Create(
            image.GetWidth(),
            image.GetHeight(),
            image.GetFormat().ToString(),
            pixels);
    }
}

internal sealed record AnimeCardBodySliceReport
{
    public string Schema { get; init; } = "scgs-anime-card-body-slice";
    public int SchemaVersion { get; init; } = 4;
    public string ApprovalStatus { get; init; } = "pending_user_approval";
    public bool UsesRealCardActor3D { get; init; } = true;
    public bool UsesPerCardSubViewport { get; init; }
    public IReadOnlyList<AnimeCardBodyCapture> Captures { get; init; } = [];
}

internal sealed record AnimeCardBodyCapture
{
    public required string State { get; init; }
    public required string File { get; init; }
    public required string Sha256 { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required AnimeCardBodyFrameStabilityEvidence FrameStability { get; init; }
    public required AnimeCardBodySliceEvidence Evidence { get; init; }
    public required AnimeCardBodyGpuReadabilityEvidence GpuReadability { get; init; }
    public required AnimeCardBodySilhouetteEvidence SilhouetteIsolation { get; init; }
}

internal sealed record AnimeCardBodyFrameStabilityEvidence
{
    public required int ConsecutiveFramePostDraws { get; init; }
    public required int AttemptCount { get; init; }
    public required string PixelFormat { get; init; }
    public required int PixelByteLength { get; init; }
    public required string FirstPixelSha256 { get; init; }
    public required string SecondPixelSha256 { get; init; }
}
