// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using System.Security.Cryptography;
using System.Text.Json;

namespace Scgs.GodotClient.Ci;

// Aggregate only: no source card, design ID, private text, token or option IDs.
internal sealed record ProductPrivacyObservation(string State, int? Viewer, ulong? Revision,
    int ViewerReads, int PrivateQueries, int Tokens, int Resources, int Collisions,
    int DragTokens, int PrivateCallbacks, int HiddenIdentities, bool InputEnabled, bool OpaqueCover);

/// <summary>
/// One explicitly revealed real product hand is poisoned in presentation only.
/// A positive GPU observation precedes normal command preparation. The actual
/// resolving and handoff render must then remove it. This component never calls
/// a native session or stores a card DTO, and never reveals another viewer.
/// </summary>
internal sealed class ProductPrivacyProbe
{
    internal const string Sentinel = "SCGS_V05_PRIVATE_GPU_PROBE_7E41D9C8";
    private readonly Node host;
    private readonly string directory;
    private readonly bool display;
    private readonly List<object> samples = [];
    private readonly HashSet<string> observed = new(StringComparer.Ordinal);
    private Task? pending;
    private Task? observationTask;
    private bool armed;
    private bool injectionVerified;
    private bool gpuInjectionVerified;
    private int injectionPixels;
    private bool complete;
    private bool observing;

    internal ProductPrivacyProbe(Node host, string absoluteDirectory)
    {
        if (!Path.IsPathFullyQualified(absoluteDirectory))
            throw new ArgumentException("Product privacy evidence requires an absolute directory.");
        this.host = host;
        directory = Path.GetFullPath(absoluteDirectory);
        display = DisplayServer.GetName() != "headless";
        Directory.CreateDirectory(directory);
        VerifyDetector();
        Write(false);
    }

    internal bool IsArmed => armed;
    internal bool VerificationPending => pending is not null || observationTask is not null || observing;

    internal Task ArmAsync(Action injectRealHand, Func<bool> stillRevealed, Func<bool> injectionIsBound)
    {
        if (armed || complete) return Task.CompletedTask;
        if (pending is not null) return pending;
        if (!stillRevealed()) throw new InvalidOperationException("Cannot inject a non-revealed product hand.");
        pending = ArmCoreAsync(injectRealHand, stillRevealed, injectionIsBound);
        return pending;
    }

    private async Task ArmCoreAsync(Action injectRealHand, Func<bool> stillRevealed, Func<bool> injectionIsBound)
    {
        try
        {
            injectRealHand();
            if (!injectionIsBound()) throw new InvalidOperationException("The real product face did not bind the private probe.");
            int previousFrame = -1;
            for (int frame = 0; frame < 2; ++frame)
            {
                await NextFrame();
                if (!stillRevealed() || !injectionIsBound())
                    throw new InvalidOperationException("Private probe source changed before command preparation.");
                if (display)
                {
                    int current = Engine.GetFramesDrawn();
                    if (previousFrame >= 0 && current != previousFrame + 1)
                        throw new InvalidOperationException("Private probe did not observe consecutive GPU frames.");
                    previousFrame = current;
                    using Image image = ReadImage();
                    injectionPixels = CountMagenta(image);
                    if (injectionPixels < 64)
                        throw new InvalidOperationException("The injected product hand texture was not visible in the real GPU image.");
                }
            }
            armed = true;
            injectionVerified = true;
            gpuInjectionVerified = display;
            Write(false);
        }
        finally { pending = null; }
    }

    internal async Task ObserveAsync(Func<ProductPrivacyObservation?> observe)
    {
        // The runner and submission boundary can arrive in the same frame.
        // Both must wait for the same complete negative check, never race past it.
        if (observationTask is not null) { await observationTask; return; }
        Task work = ObserveCoreAsync(observe);
        observationTask = work;
        try { await work; }
        finally { if (ReferenceEquals(observationTask, work)) observationTask = null; }
    }

    private async Task ObserveCoreAsync(Func<ProductPrivacyObservation?> observe)
    {
        if (pending is not null) await pending;
        if (!armed || complete) return;
        ProductPrivacyObservation? expected = observe();
        if (expected is null || expected.State is not ("resolving" or "covered") || observed.Contains(expected.State)) return;
        if (expected.State == "covered" && !observed.Contains("resolving"))
            throw new InvalidOperationException("The poisoned product command skipped resolving privacy verification.");
        observing = true;
        try
        {
            AssertSafe(expected);
            int previousFrame = -1;
            for (int ordinal = 1; ordinal <= 2; ++ordinal)
            {
                await NextFrame();
                ProductPrivacyObservation current = observe() ??
                    throw new InvalidOperationException("Product privacy state disappeared before its two-frame check.");
                if (current.State != expected.State || current.Revision != expected.Revision || current.Viewer is not null)
                    throw new InvalidOperationException("Product privacy state changed while verification held input.");
                AssertSafe(current);
                int readDelta = current.ViewerReads - expected.ViewerReads;
                int queryDelta = current.PrivateQueries - expected.PrivateQueries;
                if (readDelta != 0 || queryDelta != 0)
                    throw new InvalidOperationException("A viewer was read during protected product privacy frames.");
                string? sha256 = null;
                int width = host.GetWindow().Size.X, height = host.GetWindow().Size.Y;
                int pixels = 0;
                if (display)
                {
                    int frame = Engine.GetFramesDrawn();
                    if (previousFrame >= 0 && frame != previousFrame + 1)
                        throw new InvalidOperationException("Product privacy requires consecutive FramePostDraw frames.");
                    previousFrame = frame;
                    using Image image = ReadImage();
                    width = image.GetWidth(); height = image.GetHeight();
                    pixels = CountMagenta(image);
                    if (pixels != 0) throw new InvalidOperationException("Private magenta pixels remained in the protected GPU frame.");
                    byte[] png = image.SavePngToBuffer();
                    try
                    {
                        sha256 = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
                        File.WriteAllBytes(Path.Combine(directory, $"privacy-{current.State}-{ordinal}.png"), png);
                    }
                    finally { Array.Clear(png); }
                }
                samples.Add(new
                {
                    state = current.State, frame_ordinal = ordinal, viewer = current.Viewer, revision = current.Revision,
                    frame_clock = display ? "frame-post-draw" : "process-frame",
                    viewer_reads_delta = readDelta, private_queries_delta = queryDelta,
                    forbidden_tokens = current.Tokens, identity_resource_leaks = current.Resources,
                    collisions = current.Collisions, drag_tokens = current.DragTokens,
                    private_callbacks = current.PrivateCallbacks, hidden_identity_leaks = current.HiddenIdentities,
                    input_enabled = current.InputEnabled, opaque_cover = current.OpaqueCover,
                    gpu_checked = display, magenta_pixels = pixels, width, height, sha256,
                });
                Write(false);
            }
            observed.Add(expected.State);
        }
        finally { observing = false; }
    }

    internal void Complete()
    {
        bool success = injectionVerified && (!display || gpuInjectionVerified) &&
            observed.SetEquals(["resolving", "covered"]) && samples.Count == 4 && !VerificationPending;
        Write(success);
        if (!success) throw new InvalidOperationException("Product privacy probe was not completed on the real reveal/command/handoff path.");
        complete = true;
    }

    private static void AssertSafe(ProductPrivacyObservation sample)
    {
        if (sample.Viewer is not null || sample.Tokens != 0 || sample.Resources != 0 || sample.Collisions != 0 ||
            sample.DragTokens != 0 || sample.PrivateCallbacks != 0 || sample.HiddenIdentities != 0 ||
            sample.InputEnabled || (sample.State == "covered" && !sample.OpaqueCover))
            throw new InvalidOperationException("Product privacy scrub left identity, callbacks or input in a protected state.");
    }

    private async Task NextFrame()
    {
        if (!GodotObject.IsInstanceValid(host) || !host.IsInsideTree())
            throw new InvalidOperationException("Privacy host exited during verification.");
        await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (display) await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }

    private Image ReadImage()
    {
        Image image = host.GetViewport().GetTexture().GetImage();
        if (image.IsEmpty()) { image.Dispose(); throw new InvalidOperationException("No real product GPU image."); }
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    internal static int CountMagenta(Image image)
    {
        byte[] rgba = image.GetData();
        try { return CountMagenta(rgba); }
        finally { Array.Clear(rgba); }
    }

    private static int CountMagenta(ReadOnlySpan<byte> rgba)
    {
        int pixels = 0;
        for (int index = 0; index + 3 < rgba.Length; index += 4)
            if (rgba[index] >= 245 && rgba[index + 1] <= 12 && rgba[index + 2] >= 245 && rgba[index + 3] >= 250) ++pixels;
        return pixels;
    }

    private static void VerifyDetector()
    {
        if (CountMagenta(new byte[] { 255, 0, 255, 255 }) != 1 ||
            CountMagenta(new byte[] { 95, 40, 145, 255, 255, 255, 255, 255 }) != 0)
            throw new InvalidOperationException("Product private-pixel detector self-test failed.");
    }

    private void Write(bool success) => File.WriteAllText(Path.Combine(directory, "product-privacy.json"),
        JsonSerializer.Serialize(new
        {
            schema_version = 1, suite = "product-v05-privacy", api = "scgs_v05",
            evidence_kind = display ? "display-gpu" : "structural-only",
            injection_source = "real-revealed-product-hand", injection_verified = injectionVerified,
            detector_self_test_passed = true, gpu_injection_verified = gpuInjectionVerified,
            injection_magenta_pixels = injectionPixels, samples, success,
        }, new JsonSerializerOptions { WriteIndented = true }));
}
