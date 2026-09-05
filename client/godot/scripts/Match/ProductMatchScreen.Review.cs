// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.PresentationV2;
using Scgs.Hotseat.Product;

namespace Scgs.GodotClient.Match;

public sealed partial class ProductMatchScreen
{
    // A read-only editor acceptance probe. It never opens the reveal gate or
    // reads a session; private candidates only exist in the already revealed UI.
    public string ReviewDescribe()
    {
        if (!BattlePresentationReviewRuntime.Enabled && !CardFrameReviewRuntime.Enabled)
            return "{\"enabled\":false}";
        var state = controller?.State;
        var surfaces = new List<object>();
        if (state?.Snapshot is { } view && state.Viewer is not null)
        {
            foreach (var action in state.LegalActions)
            {
                var command = action.Command;
                if (TryFindSurface(view, command.Source, out BattlefieldSurfaceRef source) &&
                    battlefield.CiTryGetScreenAnchor(source, out Vector2 point))
                {
                    surfaces.Add(new { kind = source.Kind.ToString(), id = command.Source,
                        action = command.Action.ToString(), x = point.X, y = point.Y });
                }
            }
        }
        return System.Text.Json.JsonSerializer.Serialize(new {
            enabled = true,
            review_entry = CardFrameReviewRuntime.Enabled ? "card-frame-review" : "battle-presentation-review",
            synthetic = false,
            candidate_battle_presentation_enabled = BattlePresentationReviewRuntime.Enabled,
            presentation_playback_enabled = controller?.PresentationEnabled ?? false,
            presentation_effects_revision = "battle-presentation-v2-stage1-unchanged",
            mode = state?.Mode.ToString(), revision = state?.Interaction.Revision,
            viewer = state?.Viewer?.ToString(), step = state?.Interaction.Step.ToString(),
            cue = presentationDirector?.CurrentCueKind, progress = presentationDirector?.CurrentCueProgress,
            surfaces = surfaces.Distinct().ToArray(),
        });
    }
}
