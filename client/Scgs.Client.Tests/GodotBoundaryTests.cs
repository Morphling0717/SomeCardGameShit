// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scgs.GodotClient.Match;
using Scgs.GodotClient.Native;

namespace Scgs.Client.Tests;

[TestClass]
public sealed class GodotBoundaryTests
{
    [TestMethod]
    public void ProductDecksAreTheInteractiveDefaultsWhileProtocolFixturesRemainExplicit()
    {
        Assert.AreEqual(
            "oathguard_luminous_oath_v1",
            MatchSetup.ProductDefaults.Player0Deck);
        Assert.AreEqual(
            "pactmage_abyssal_pact_v1",
            MatchSetup.ProductDefaults.Player1Deck);
        Assert.AreSame(MatchSetup.ProductDefaults, MatchSetup.Defaults);
        Assert.AreEqual("synthetic_alpha", MatchSetup.LegacyDefaults.Player0Deck);
        Assert.AreEqual("synthetic_beta", MatchSetup.LegacyDefaults.Player1Deck);
    }

    [TestMethod]
    public void WindowsAndMacEditorAndExportLayoutsResolveOnlyKnownLocations()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "scgs-gate3-layout"));
        string applicationBase = Path.Combine(root, "managed-data");
        string projectNative = Path.Combine(root, "project", "native");

        string windowsExecutable = Path.Combine(root, "windows-export", "SomeCardGameShit.exe");
        IReadOnlyList<string> windows = NativeLibraryLayout.CandidatePaths(
            GodotDesktopTarget.WindowsX64,
            windowsExecutable,
            applicationBase,
            projectNative);
        Assert.AreEqual(
            Path.Combine(root, "windows-export", "scgs_v05.dll"),
            windows[0]);
        CollectionAssert.Contains(
            windows.ToArray(),
            Path.Combine(projectNative, "windows-x86_64", "scgs_v05.dll"));

        string macExecutable = Path.Combine(
            root,
            "SomeCardGameShit.app",
            "Contents",
            "MacOS",
            "SomeCardGameShit");
        IReadOnlyList<string> mac = NativeLibraryLayout.CandidatePaths(
            GodotDesktopTarget.MacOsArm64,
            macExecutable,
            applicationBase,
            projectNative);
        CollectionAssert.Contains(
            mac.ToArray(),
            Path.Combine(
                root,
                "SomeCardGameShit.app",
                "Contents",
                "Frameworks",
                "libscgs_v05.dylib"));
        CollectionAssert.Contains(
            mac.ToArray(),
            Path.Combine(projectNative, "macos-arm64", "libscgs_v05.dylib"));

        Assert.IsFalse(windows.Any(path => path == Environment.CurrentDirectory));
        Assert.IsFalse(mac.Any(path => path == Environment.CurrentDirectory));
    }

    [TestMethod]
    public void ProductNativeLibraryLayoutDefaultsToV05WhileV04RequiresExplicitCompatibilitySelection()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "scgs-product-layout"));
        string executable = Path.Combine(root, "export", "SomeCardGameShit.exe");
        string applicationBase = Path.Combine(root, "managed");
        string projectNative = Path.Combine(root, "project", "native");

        IReadOnlyList<string> product = NativeLibraryLayout.CandidatePaths(
            GodotDesktopTarget.WindowsX64,
            executable,
            applicationBase,
            projectNative);
        IReadOnlyList<string> legacy = NativeLibraryLayout.CandidatePaths(
            GodotDesktopTarget.WindowsX64,
            executable,
            applicationBase,
            projectNative,
            ScgsNativeApiGeneration.LegacyV04);

        Assert.IsTrue(product.All(path =>
            path.EndsWith("scgs_v05.dll", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(legacy.All(path =>
            path.EndsWith("scgs_v04.dll", StringComparison.OrdinalIgnoreCase)));
        Assert.AreEqual(
            ("windows-x86_64", "scgs_v05.dll"),
            NativeLibraryLayout.Describe(
                GodotDesktopTarget.WindowsX64,
                ScgsNativeApiGeneration.ProductV05));
        Assert.AreEqual(
            ("macos-arm64", "libscgs_v05.dylib"),
            NativeLibraryLayout.Describe(
                GodotDesktopTarget.MacOsArm64,
                ScgsNativeApiGeneration.ProductV05));
    }

    [TestMethod]
    public void ViewerRevealGateNeverRequestsASnapshotBeforeExplicitReveal()
    {
        var session = new CountingSession();
        var gate = new ViewerRevealGate(session);

        gate.Cover(PlayerId.Player0);
        Assert.AreEqual(0, session.GetViewCalls);
        Assert.AreEqual(0, gate.GetViewCallCount);
        Assert.IsFalse(gate.IsRevealed);

        MatchView snapshot = gate.RevealAndGetView();
        Assert.AreEqual(PlayerId.Player0, snapshot.Viewer);
        Assert.AreEqual(1, session.GetViewCalls);
        Assert.AreEqual(1, gate.GetViewCallCount);
        Assert.IsTrue(gate.IsRevealed);
        Assert.ThrowsExactly<InvalidOperationException>(() => gate.RevealAndGetView());
        Assert.AreEqual(1, session.GetViewCalls);

        gate.Cover(PlayerId.Player1);
        Assert.AreEqual(1, session.GetViewCalls);
        Assert.AreEqual(PlayerId.Player1, gate.RevealAndGetView().Viewer);
        Assert.AreEqual(2, session.GetViewCalls);
    }

    private sealed class CountingSession : IScgsGameSession
    {
        public int GetViewCalls { get; private set; }

        public MatchView GetView(PlayerId viewer)
        {
            ++GetViewCalls;
            return new MatchView
            {
                Viewer = viewer,
                ActivePlayer = PlayerId.Player0,
                FirstPlayer = PlayerId.Player0,
                RandomSeed = 1,
                Phase = MatchPhase.Mulligan,
                Result = GameResult.Ongoing,
                Revision = 0,
                Players = [],
                Reaction = null!,
            };
        }

        public void Dispose()
        {
        }

        public EngineStatus Start() => throw new NotSupportedException();

        public LegalActionsResult ListLegalActions(ActionQueryRequest query) =>
            throw new NotSupportedException();

        public ValidTargetsResult ListValidTargets(ActionQueryRequest query) =>
            throw new NotSupportedException();

        public ValidSlotsResult ListValidSlots(ActionQueryRequest query) =>
            throw new NotSupportedException();

        public ValidDonorsResult ListValidDonors(ActionQueryRequest query) =>
            throw new NotSupportedException();

        public PaymentResult PreviewPayment(GameCommandRequest command) =>
            throw new NotSupportedException();

        public ReactionContext GetReactionContext(PlayerId viewer) =>
            throw new NotSupportedException();

        public EngineStatus SubmitCommand(GameCommandRequest command) =>
            throw new NotSupportedException();

        public EventBatch ReadEvents(PlayerId viewer, ulong afterSequence) =>
            throw new NotSupportedException();

        public EventBatch ReadNewEvents(PlayerId viewer) => throw new NotSupportedException();

        public ulong GetEventCursor(PlayerId viewer) => throw new NotSupportedException();
    }
}
