using Scgs.Client;

namespace Scgs.GodotClient.Match;

/// <summary>
/// The only object in the match UI that is allowed to request viewer-scoped
/// snapshots. Cover() forgets the prior snapshot. RevealAndGetView() is the
/// sole transition that performs GetView, after the privacy overlay has
/// emitted an explicit reveal request.
/// </summary>
internal sealed class ViewerRevealGate
{
    private readonly IScgsGameSession _session;
    private PlayerId? _coveredViewer;

    public ViewerRevealGate(IScgsGameSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool IsRevealed { get; private set; }

    public PlayerId? RevealedViewer { get; private set; }

    internal int GetViewCallCount { get; private set; }

    public void Cover(PlayerId nextViewer)
    {
        _coveredViewer = nextViewer;
        RevealedViewer = null;
        IsRevealed = false;
    }

    public MatchView RevealAndGetView()
    {
        if (_coveredViewer is not { } viewer)
        {
            throw new InvalidOperationException("A viewer must be covered before it can be revealed.");
        }

        if (IsRevealed)
        {
            throw new InvalidOperationException("The current viewer is already revealed.");
        }

        // This call is deliberately reachable only through the reveal
        // transition. MatchScreen never retains a direct session reference.
        GetViewCallCount++;
        MatchView view = _session.GetView(viewer);
        if (view.Viewer != viewer)
        {
            throw new InvalidOperationException("The native snapshot viewer does not match the reveal gate.");
        }

        RevealedViewer = viewer;
        IsRevealed = true;
        return view;
    }
}
