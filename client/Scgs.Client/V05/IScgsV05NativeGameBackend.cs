// SPDX-License-Identifier: GPL-3.0-or-later
namespace Scgs.Client.V05;

internal interface IScgsV05NativeGameBackend : IDisposable
{
    EngineStatus Start();

    string GetView(PlayerId viewer);

    string ListLegalActions(string queryJson);

    string ListValidTargets(string queryJson);

    string ListValidSlots(string queryJson);

    string ListValidDonors(string queryJson);

    string PreviewPayment(string commandJson);

    string GetReactionContext(PlayerId viewer);

    EngineStatus SubmitCommand(string commandJson);

    string ReadEvents(PlayerId viewer, ulong afterSequence);
}
