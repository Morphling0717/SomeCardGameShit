// SPDX-License-Identifier: GPL-3.0-or-later
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scgs.Hotseat.Product;
using V05 = Scgs.Client.V05;

namespace Scgs.Hotseat.ProductReview;

public enum PresentationReviewKind
{
    Oathguard,
    Pactmage,
    Spell,
}

public sealed record PresentationReviewTraceEntry(
    int Index,
    V05.GameCommandRequest Command,
    ulong RevisionAfter,
    string Sha256);

/// <summary>
/// A real, legitimately reached match. This is development evidence, not a saved
/// product match or a substitute for hotseat Covered -> Reveal. The command
/// trace may contain the preparer's private option IDs: keep it in dev reports,
/// never display it in the player's battlefield or public presentation queue.
/// </summary>
public sealed class PreparedPresentationReview : IDisposable
{
    private V05.IScgsV05GameSession? session;

    internal PreparedPresentationReview(
        V05.IScgsV05GameSession session,
        PresentationReviewKind kind,
        V05.GameConfigRequest config,
        string implementationSha,
        string initialSha256,
        IReadOnlyList<PresentationReviewTraceEntry> trace,
        V05.LegalAction readyAction)
    {
        this.session = session;
        Kind = kind;
        Config = config;
        ImplementationSha = implementationSha;
        InitialSha256 = initialSha256;
        Trace = trace;
        ReadyAction = readyAction;
    }

    public PresentationReviewKind Kind { get; }
    public V05.GameConfigRequest Config { get; }
    public string ImplementationSha { get; }
    public string InitialSha256 { get; }
    public IReadOnlyList<PresentationReviewTraceEntry> Trace { get; }
    public string TraceSha256 => Trace.Count == 0 ? InitialSha256 : Trace[^1].Sha256;
    public V05.PlayerId Viewer => V05.PlayerId.Player0;
    public V05.LegalAction ReadyAction { get; }

    // The receiver constructs its normal controller, which starts Covered.
    // Taking ownership never reads a snapshot or advances an event cursor.
    public V05.IScgsV05GameSession TakeSession()
    {
        V05.IScgsV05GameSession result = session ??
            throw new InvalidOperationException("The review session has already been transferred or disposed.");
        session = null;
        return result;
    }

    public void Dispose()
    {
        session?.Dispose();
        session = null;
    }
}

/// <summary>
/// Prepares fixed-deck review scenes exclusively with viewer-safe snapshots,
/// enumerated legal commands and the ordinary native submission API. No card
/// definitions, deck order or engine state are injected. Shuffle reproducibility
/// is scoped to the same native toolchain; the seed and complete trace are kept.
/// </summary>
public static class PresentationReviewScenario
{
    public const string OathguardDeck = "oathguard_luminous_oath_v1";
    public const string PactmageDeck = "pactmage_abyssal_pact_v1";
    public const int MaximumCommands = 96;
    private static readonly JsonSerializerOptions TraceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PreparedPresentationReview Prepare(
        PresentationReviewKind kind,
        Func<V05.GameConfigRequest, V05.IScgsV05GameSession> createSession,
        string implementationSha,
        uint seedStart = 1,
        int maximumSeedAttempts = 64)
    {
        ArgumentNullException.ThrowIfNull(createSession);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (implementationSha is null || implementationSha.Length != 40 ||
            implementationSha.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("A full implementation commit SHA is required.", nameof(implementationSha));
        }

        if (maximumSeedAttempts is < 1 or > 256 ||
            (ulong)seedStart + (uint)maximumSeedAttempts - 1 > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSeedAttempts));
        }

        for (int attempt = 0; attempt < maximumSeedAttempts; ++attempt)
        {
            var config = new V05.GameConfigRequest(
                kind == PresentationReviewKind.Pactmage ? PactmageDeck : OathguardDeck,
                kind == PresentationReviewKind.Pactmage ? OathguardDeck : PactmageDeck)
            {
                RandomSeed = seedStart + (uint)attempt,
                FirstPlayerMode = V05.FirstPlayerMode.Player0,
                ShuffleDecks = true,
            };
            V05.IScgsV05GameSession session = createSession(config) ??
                throw new InvalidOperationException("The review session factory returned null.");
            bool transferred = false;
            try
            {
                V05.EngineStatus started = session.Start();
                if (!started.IsSuccess)
                {
                    throw new InvalidOperationException($"Review start failed: {started.Code}.");
                }

                V05.MatchView opening = session.GetView(V05.PlayerId.Player0);
                if (!opening.Players[0].Hand.Any(card => card.DesignId == DesignId(kind)))
                {
                    continue;
                }

                string initialHash = InitialHash(kind, config, implementationSha);
                var trace = new List<PresentationReviewTraceEntry>();
                V05.LegalAction? ready = PrepareCandidate(session, kind, initialHash, trace);
                if (ready is null)
                {
                    continue;
                }

                // A preparer never uses ReadNewEvents: neither viewer's cursor
                // has been consumed when the real UI takes this session over.
                if (session.GetEventCursor(V05.PlayerId.Player0) != 0 ||
                    session.GetEventCursor(V05.PlayerId.Player1) != 0)
                {
                    throw new InvalidOperationException("Preparation consumed a hotseat event cursor.");
                }

                transferred = true;
                return new PreparedPresentationReview(
                    session, kind, config, implementationSha.ToLowerInvariant(), initialHash,
                    trace.AsReadOnly(), ready);
            }
            finally
            {
                if (!transferred)
                {
                    session.Dispose();
                }
            }
        }

        throw new InvalidOperationException(
            $"No legal {kind} review scene reached within {maximumSeedAttempts} seeds and {MaximumCommands} commands per seed.");
    }

    public static string DesignId(PresentationReviewKind kind) => kind switch
    {
        PresentationReviewKind.Oathguard => "LO-11",
        PresentationReviewKind.Pactmage => "AP-11",
        PresentationReviewKind.Spell => "NT-04",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static bool ValidateTrace(PreparedPresentationReview prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        string previous = InitialHash(prepared.Kind, prepared.Config, prepared.ImplementationSha);
        if (previous != prepared.InitialSha256)
        {
            return false;
        }

        ulong? priorRevision = null;
        for (int index = 0; index < prepared.Trace.Count; ++index)
        {
            PresentationReviewTraceEntry entry = prepared.Trace[index];
            if (entry.Index != index ||
                entry.RevisionAfter != entry.Command.ExpectedRevision + 1 ||
                (priorRevision.HasValue && entry.Command.ExpectedRevision != priorRevision) ||
                entry.Sha256 != CommandHash(previous, entry.Index, entry.Command, entry.RevisionAfter))
            {
                return false;
            }

            previous = entry.Sha256;
            priorRevision = entry.RevisionAfter;
        }

        return priorRevision == prepared.ReadyAction.Command.ExpectedRevision;
    }

    private static V05.LegalAction? PrepareCandidate(
        V05.IScgsV05GameSession session,
        PresentationReviewKind kind,
        string initialHash,
        List<PresentationReviewTraceEntry> trace)
    {
        string previousHash = initialHash;
        for (int step = 0; step < MaximumCommands; ++step)
        {
            V05.MatchView routing = session.GetView(V05.PlayerId.Player0);
            V05.PlayerId? actor = ProductHotseatMatchController.DetermineActor(routing);
            if (!actor.HasValue || routing.Result != V05.GameResult.Ongoing)
            {
                return null;
            }

            V05.MatchView view = actor == V05.PlayerId.Player0 ? routing : session.GetView(actor.Value);
            V05.LegalActionsResult actions = session.ListLegalActions(new V05.ActionQueryRequest(actor.Value, view.Revision));
            if (actions.Revision != view.Revision ||
                actions.Actions.Any(action => action.Command.ExpectedRevision != view.Revision))
            {
                throw new InvalidOperationException("Review query returned a stale legal action.");
            }

            if (actor == V05.PlayerId.Player0 && view.Phase == V05.MatchPhase.Action &&
                !view.PendingChoice.Pending)
            {
                V05.CardView? card = view.Players[0].Hand.FirstOrDefault(item => item.DesignId == DesignId(kind));
                V05.LegalAction? ready = actions.Actions.FirstOrDefault(action =>
                    card?.InstanceId == action.Command.Source && IsReadyAction(kind, action, view));
                if (ready is not null)
                {
                    return ready;
                }
            }

            V05.LegalAction? next = SelectPreparationAction(kind, view, actions.Actions);
            if (next is null)
            {
                return null;
            }

            V05.GameCommandRequest command = FreezeCommand(next.Command);
            V05.EngineStatus result = session.SubmitCommand(command);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"An enumerated review command failed: {result.Code}.");
            }

            ulong after = session.GetView(actor.Value).Revision;
            if (after != view.Revision + 1)
            {
                throw new InvalidOperationException("A review command did not advance exactly one revision.");
            }

            previousHash = CommandHash(previousHash, trace.Count, command, after);
            trace.Add(new PresentationReviewTraceEntry(trace.Count, command, after, previousHash));
        }

        return null;
    }

    private static bool IsReadyAction(
        PresentationReviewKind kind,
        V05.LegalAction action,
        V05.MatchView view)
    {
        if (action.Command.UseAdvance || !action.Payment.Status.IsSuccess)
        {
            return false;
        }

        if (kind == PresentationReviewKind.Spell)
        {
            V05.Target? target = action.Command.Target;
            return action.Command.Action == V05.ActionKind.CastSpell &&
                target?.Kind == V05.TargetKind.Permanent && target.Player == V05.PlayerId.Player1 &&
                view.Players[1].MainBoard.Any(card =>
                    card?.InstanceId == target.Permanent && card?.Kind == V05.CardKind.Follower);
        }

        // These are review readiness requirements on public resources, not a
        // second legality implementation. The actual move is enumerated above;
        // integration tests independently submit it and enumerate its evolution.
        V05.PlayerView player = view.Players[0];
        return action.Command.Action == V05.ActionKind.PlayUnit && player.EvolutionEnergy > 0 &&
            !player.EvolutionUsedThisTurn;
    }

    private static V05.LegalAction? SelectPreparationAction(
        PresentationReviewKind kind,
        V05.MatchView view,
        IReadOnlyList<V05.LegalAction> actions)
    {
        if (view.PendingChoice.Pending)
        {
            return actions.FirstOrDefault(action => action.Command.Action == V05.ActionKind.ResolveChoice);
        }

        if (view.Phase == V05.MatchPhase.Mulligan)
        {
            return actions.FirstOrDefault(action => action.Command.Action == V05.ActionKind.Mulligan &&
                action.Command.MulliganCards.Count == 0);
        }

        if (view.Phase == V05.MatchPhase.Reaction)
        {
            return actions.FirstOrDefault(action => action.Command.Action == V05.ActionKind.PassReaction);
        }

        // Only the spell scene needs one opposing, honestly played target. All
        // other turns pass: no attacks, concessions, resource cheats or deck edits.
        if (kind == PresentationReviewKind.Spell && view.Viewer == V05.PlayerId.Player1 &&
            !view.Players[1].MainBoard.Any(card => card?.Kind == V05.CardKind.Follower))
        {
            V05.LegalAction? target = actions.FirstOrDefault(action =>
                action.Command.Action == V05.ActionKind.PlayUnit && !action.Command.UseAdvance);
            if (target is not null)
            {
                return target;
            }
        }

        return actions.FirstOrDefault(action => action.Command.Action == V05.ActionKind.EndTurn);
    }

    private static V05.GameCommandRequest FreezeCommand(V05.GameCommandRequest command) => command with
    {
        MulliganCards = Array.AsReadOnly(command.MulliganCards.ToArray()),
        SelectedOptionIds = Array.AsReadOnly(command.SelectedOptionIds.ToArray()),
        AdditionalCostCards = Array.AsReadOnly(command.AdditionalCostCards.ToArray()),
    };

    private static string InitialHash(
        PresentationReviewKind kind,
        V05.GameConfigRequest config,
        string implementationSha) => Hash(JsonSerializer.Serialize(new
        {
            suite = "product-presentation-review-v1",
            implementation_sha = implementationSha.ToLowerInvariant(),
            kind = kind.ToString().ToLowerInvariant(),
            config,
        }, TraceJsonOptions));

    private static string CommandHash(string previous, int index, V05.GameCommandRequest command, ulong revisionAfter) =>
        Hash(previous + "\n" + JsonSerializer.Serialize(new { index, command, revision_after = revisionAfter }, TraceJsonOptions));

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
