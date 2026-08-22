// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client;

public sealed class ScgsGameSession : IScgsGameSession
{
    private readonly object sync = new();
    private readonly IScgsNativeGameBackend backend;
    private readonly ulong[] eventCursors = new ulong[2];
    private bool disposed;

    private ScgsGameSession(IScgsNativeGameBackend backend)
    {
        this.backend = backend;
    }

    public static ScgsGameSession Create(
        GameConfigRequest config,
        string absoluteNativeLibraryPath) =>
        new(ScgsV04NativeBackend.Create(config, absoluteNativeLibraryPath));

    internal static ScgsGameSession CreateForTesting(IScgsNativeGameBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return new ScgsGameSession(backend);
    }

    public EngineStatus Start() => Execute(() => backend.Start());

    public MatchView GetView(PlayerId viewer) => Execute(() =>
    {
        ValidatePlayer(viewer);
        ViewEnvelope envelope = ScgsJson.DeserializeView(backend.GetView(viewer), viewer);
        return envelope.View;
    });

    public LegalActionsResult ListLegalActions(ActionQueryRequest query) => Execute(() =>
    {
        string request = ScgsJson.SerializeQuery(query);
        ActionsEnvelope envelope = ScgsJson.DeserializeActions(backend.ListLegalActions(request));
        return new LegalActionsResult(envelope.Revision, Array.AsReadOnly(envelope.Actions));
    });

    public ValidTargetsResult ListValidTargets(ActionQueryRequest query) => Execute(() =>
    {
        string request = ScgsJson.SerializeQuery(query);
        TargetsEnvelope envelope = ScgsJson.DeserializeTargets(backend.ListValidTargets(request));
        return new ValidTargetsResult(envelope.Revision, Array.AsReadOnly(envelope.Targets));
    });

    public ValidSlotsResult ListValidSlots(ActionQueryRequest query) => Execute(() =>
    {
        string request = ScgsJson.SerializeQuery(query);
        SlotsEnvelope envelope = ScgsJson.DeserializeSlots(backend.ListValidSlots(request));
        return new ValidSlotsResult(envelope.Revision, Array.AsReadOnly(envelope.Slots));
    });

    public ValidDonorsResult ListValidDonors(ActionQueryRequest query) => Execute(() =>
    {
        string request = ScgsJson.SerializeQuery(query);
        DonorsEnvelope envelope = ScgsJson.DeserializeDonors(backend.ListValidDonors(request));
        return new ValidDonorsResult(envelope.Revision, Array.AsReadOnly(envelope.Donors));
    });

    public PaymentResult PreviewPayment(GameCommandRequest command) => Execute(() =>
    {
        string request = ScgsJson.SerializeCommand(command);
        PaymentEnvelope envelope = ScgsJson.DeserializePayment(backend.PreviewPayment(request));
        return new PaymentResult(envelope.Revision, envelope.Payment);
    });

    public ReactionContext GetReactionContext(PlayerId viewer) => Execute(() =>
    {
        ValidatePlayer(viewer);
        ReactionEnvelope envelope = ScgsJson.DeserializeReaction(
            backend.GetReactionContext(viewer),
            viewer);
        return envelope.Reaction;
    });

    public EngineStatus SubmitCommand(GameCommandRequest command) => Execute(() =>
        backend.SubmitCommand(ScgsJson.SerializeCommand(command)));

    public EventBatch ReadEvents(PlayerId viewer, ulong afterSequence) => Execute(() =>
    {
        ValidatePlayer(viewer);
        EventsEnvelope envelope = ScgsJson.DeserializeEvents(
            backend.ReadEvents(viewer, afterSequence),
            viewer,
            afterSequence);
        return ToEventBatch(envelope);
    });

    public EventBatch ReadNewEvents(PlayerId viewer) => Execute(() =>
    {
        int index = PlayerIndex(viewer);
        ulong afterSequence = eventCursors[index];
        EventsEnvelope envelope = ScgsJson.DeserializeEvents(
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
        new(
            envelope.Revision,
            envelope.LastSequence,
            Array.AsReadOnly(envelope.Events));

    private static int PlayerIndex(PlayerId player) => player switch
    {
        PlayerId.Player0 => 0,
        PlayerId.Player1 => 1,
        _ => throw new ArgumentOutOfRangeException(
            nameof(player),
            player,
            "Unsupported player value."),
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
