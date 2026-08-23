// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.Client;
using Scgs.Hotseat;

namespace Scgs.GodotClient.Battlefield;

public interface IBattlefieldPresenter
{
    event EventHandler<BattlefieldSurfaceGestureEventArgs>? SurfaceGestureRequested;

    event EventHandler<BattlefieldSurfaceHoverEventArgs>? SurfaceHovered;

    event EventHandler<BattlefieldSurfaceHoverEventArgs>? SurfaceSecondaryRequested;

    event EventHandler? ProjectionChanged;

    bool InputEnabled { get; }

    ulong Revision { get; }

    PlayerId PerspectiveViewer { get; }

    void RenderPrivate(MatchView view, HotseatInteractionContext interaction);

    void RenderPublic(HotseatPublicBoardView board, PlayerId perspectiveViewer);

    bool TryConfigureInteraction(
        ulong revision,
        IEnumerable<BattlefieldInteractionSurface> surfaces,
        BattlefieldSurfaceRef? selected = null,
        BattlefieldSurfaceRef? targetingSource = null);

    void SetInputEnabled(bool enabled);

    void SetViewportInsets(float leftPixels, float rightPixels);

    void SetViewportObstructions(
        Control? leftControl,
        Control? rightControl,
        float paddingPixels = 16.0f);

    void SetGuiBlocker(Func<Vector2, bool>? guiBlocksPointer);

    bool TryGetWorldAnchor(BattlefieldSurfaceRef surface, out Vector3 anchor);

    bool TryGetSurfaceAtScreen(Vector2 screenPosition, out BattlefieldSurfaceRef surface);

    bool FocusNextSurface(int direction);

    bool ActivateFocusedSurface();

    void ClearSensitive();
}
