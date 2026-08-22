// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client;

public interface IScgsGameSession : IDisposable
{
    EngineStatus Start();

    MatchView GetView(PlayerId viewer);

    LegalActionsResult ListLegalActions(ActionQueryRequest query);

    ValidTargetsResult ListValidTargets(ActionQueryRequest query);

    ValidSlotsResult ListValidSlots(ActionQueryRequest query);

    ValidDonorsResult ListValidDonors(ActionQueryRequest query);

    PaymentResult PreviewPayment(GameCommandRequest command);

    ReactionContext GetReactionContext(PlayerId viewer);

    EngineStatus SubmitCommand(GameCommandRequest command);

    EventBatch ReadEvents(PlayerId viewer, ulong afterSequence);

    EventBatch ReadNewEvents(PlayerId viewer);

    ulong GetEventCursor(PlayerId viewer);
}
