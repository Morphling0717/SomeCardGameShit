// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client.V05;

public sealed class ScgsV05GameSession : IScgsV05GameSession
{
    private readonly object sync = new();
    private readonly IScgsV05NativeGameBackend backend;
    private readonly ulong[] eventCursors = new ulong[2];
    private bool disposed;

    private ScgsV05GameSession(IScgsV05NativeGameBackend backend)
    {
        this.backend = backend;
    }

    public static ScgsV05GameSession Create(
        GameConfigRequest config,
        string absoluteNativeLibraryPath) =>
        new(ScgsV05NativeBackend.Create(config, absoluteNativeLibraryPath));

    internal static ScgsV05GameSession CreateForTesting(IScgsV05NativeGameBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return new ScgsV05GameSession(backend);
    }

    public EngineStatus Start() => Execute(() => backend.Start());

    public MatchView GetView(PlayerId viewer) => Execute(() =>
    {
        ValidatePlayer(viewer);
        return ScgsV05Json.DeserializeView(backend.GetView(viewer), viewer);
    });

    public LegalActionsResult ListLegalActions(ActionQueryRequest query) => Execute(() =>
    {
        string request = ScgsV05Json.SerializeQuery(query);
        ActionsEnvelope envelope = ScgsV05Json.DeserializeActions(backend.ListLegalActions(request));
        return new LegalActionsResult(envelope.Revision, Array.AsReadOnly(envelope.Actions));
    });

    public ValidTargetsResult ListValidTargets(ActionQueryRequest query) => Execute(() =>
    {
        string request = ScgsV05Json.SerializeQuery(query);
        TargetsEnvelope envelope = ScgsV05Json.DeserializeTargets(backend.ListValidTargets(request));
        return new ValidTargetsResult(envelope.Revision, Array.AsReadOnly(envelope.Targets));
    });

    public ValidSlotsResult ListValidSlots(ActionQueryRequest query) => Execute(() =>
    {
        string request = ScgsV05Json.SerializeQuery(query);
        SlotsEnvelope envelope = ScgsV05Json.DeserializeSlots(backend.ListValidSlots(request));
        return new ValidSlotsResult(envelope.Revision, Array.AsReadOnly(envelope.Slots));
    });

    public ValidDonorsResult ListValidDonors(ActionQueryRequest query) => Execute(() =>
    {
        string request = ScgsV05Json.SerializeQuery(query);
        DonorsEnvelope envelope = ScgsV05Json.DeserializeDonors(backend.ListValidDonors(request));
        return new ValidDonorsResult(envelope.Revision, Array.AsReadOnly(envelope.Donors));
    });

    public PaymentResult PreviewPayment(GameCommandRequest command) => Execute(() =>
    {
        string request = ScgsV05Json.SerializeCommand(command);
        PaymentEnvelope envelope = ScgsV05Json.DeserializePayment(backend.PreviewPayment(request));
        return new PaymentResult(envelope.Revision, envelope.Payment);
    });

    public ReactionAndChoiceResult GetReactionContext(PlayerId viewer) => Execute(() =>
    {
        ValidatePlayer(viewer);
        ReactionEnvelope envelope = ScgsV05Json.DeserializeReaction(
            backend.GetReactionContext(viewer),
            viewer);
        return new ReactionAndChoiceResult(
            envelope.Revision,
            envelope.Reaction,
            envelope.PendingChoice);
    });

    public EngineStatus SubmitCommand(GameCommandRequest command) => Execute(() =>
        backend.SubmitCommand(ScgsV05Json.SerializeCommand(command)));

    public EventBatch ReadEvents(PlayerId viewer, ulong afterSequence) => Execute(() =>
    {
        ValidatePlayer(viewer);
        EventsEnvelope envelope = ScgsV05Json.DeserializeEvents(
            backend.ReadEvents(viewer, afterSequence),
            viewer,
            afterSequence);
        return ToEventBatch(envelope);
    });

    public EventBatch ReadNewEvents(PlayerId viewer) => Execute(() =>
    {
        int index = PlayerIndex(viewer);
        ulong afterSequence = eventCursors[index];
        EventsEnvelope envelope = ScgsV05Json.DeserializeEvents(
            backend.ReadEvents(viewer, afterSequence),
            viewer,
            afterSequence);
        eventCursors[index] = envelope.LastSequence;
        return ToEventBatch(envelope);
    });

    public ulong GetEventCursor(PlayerId viewer) => Execute(() =>
        eventCursors[PlayerIndex(viewer)]);

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            backend.Dispose();
        }
    }

    private static EventBatch ToEventBatch(EventsEnvelope envelope) =>
        new(envelope.Revision, envelope.LastSequence, Array.AsReadOnly(envelope.Events));

    private static int PlayerIndex(PlayerId player) => player switch
    {
        PlayerId.Player0 => 0,
        PlayerId.Player1 => 1,
        _ => throw new ArgumentOutOfRangeException(
            nameof(player),
            player,
            "Unsupported v05 player value."),
    };

    private static void ValidatePlayer(PlayerId player) => _ = PlayerIndex(player);

    private T Execute<T>(Func<T> action)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return action();
        }
    }
}
