// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Json;
using Godot;
using Scgs.GodotClient.Battlefield;

namespace Scgs.GodotClient.PresentationV2;

internal sealed partial class ProductPresentationDirector
{
    /// <summary>
    /// Read-only real-node evidence for an Editor MCP observation. Visible face
    /// data is reported only if already rendered; hidden actors have no face.
    /// No controller, session, or prospective viewer is read by this probe.
    /// </summary>
    public string CiInspectPresentationSafety()
    {
        if (!BattlePresentationReviewRuntime.Enabled)
            return "{\"available\":false,\"reason\":\"review_entry_required\"}";
        Node? board = _presenter;
        board ??= GetParent()?.GetNodeOrNull<Battlefield3DPresenter>("%Battlefield3D");
        if (board is null && _fx is not null && GodotObject.IsInstanceValid(_fx)) board = _fx.GetParent();
        CardActor3D[] cards = board is null ? [] : Descendants(board).OfType<CardActor3D>().ToArray();
        CardActor3D[] moving = cards.Where(card => card.Name.ToString().StartsWith("PublicMotion", StringComparison.Ordinal)).ToArray();
        Camera3D? camera = GetViewport().GetCamera3D();
        object[] roster = cards.Where(card => card.IsVisibleInTree() && card.CiProductFace is not null)
            .Select(card => DescribeCard(card, camera)).ToArray();
        bool cutinBound = _cutinPortrait is not null && GodotObject.IsInstanceValid(_cutinPortrait) &&
            _cutinPortrait.Texture is not null;
        bool cutinVisible = _cutinRoot is not null && GodotObject.IsInstanceValid(_cutinRoot) &&
            _cutinRoot.IsVisibleInTree();
        return JsonSerializer.Serialize(new
        {
            available = true,
            playing = IsPlaying,
            cancelled = LastPlaybackCancelled,
            fast_forwarded = LastPlaybackFastForwarded,
            cue_kind = CurrentCueKind,
            cue_sequence = CurrentCueSequence,
            cue_progress = CurrentCueProgress,
            animated_events = LastAnimatedEventCount,
            pooled_motion_cards = moving.Length,
            motion_identity_bindings = moving.Count(card => card.CiProductFace is not null),
            motion_visible = moving.Count(card => card.IsVisibleInTree()),
            motion_collisions = moving.Count(card => card.CollisionLayer != 0 || card.Surface is not null),
            cutin_texture_bound = cutinBound,
            cutin_visible = cutinVisible,
            idle_motion_clean = !IsPlaying && moving.All(card => card.CiProductFace is null &&
                !card.Visible && card.CollisionLayer == 0 && card.Surface is null) && !cutinBound && !cutinVisible,
            visible_cards = roster,
        });
    }

    /// <summary>
    /// Destructive to the cosmetic test only: call in the explicit review lane,
    /// then restart that test session. It must not complete/ACK a native action.
    /// </summary>
    public void CiCancelPresentationForTest()
    {
        if (BattlePresentationReviewRuntime.Enabled) Cancel();
    }

    public void CiSkipPresentationForTest()
    {
        if (BattlePresentationReviewRuntime.Enabled) Skip();
    }

    private static object DescribeCard(CardActor3D card, Camera3D? camera)
    {
        CardReadabilityEvidence local = card.CiReadabilityEvidence;
        CardGpuReadabilityEvidence? gpu = camera is null ? null : card.CiGpuReadabilityEvidence(camera);
        CardNameGpuEvidence? name = camera is null ? null : card.CiProductNameGpuEvidence(camera);
        return new
        {
            node = card.GetPath().ToString(),
            design_id = card.CiProductFace!.ViewModel.DesignId,
            name = local.NameText,
            integrated_face = card.CiUsesIntegratedProductFace,
            composition_readable = local.MatchesExpectedComposition,
            cost = Badge(local.CostBadge, gpu?.CostBadge),
            attack = Badge(local.AttackBadge, gpu?.AttackBadge),
            health = Badge(local.HealthBadge, gpu?.HealthBadge),
            countdown = Badge(local.CountdownBadge, gpu?.CountdownBadge),
            name_rect = Rectangle(name?.ScreenRect ?? new Rect2()),
            name_socket = Rectangle(name?.TextSocketScreenRect ?? new Rect2()),
        };
    }

    private static object Badge(CardBadgeReadabilityEvidence local, CardBadgeGpuEvidence? gpu) => new
    {
        text = local.Text,
        label_visible = local.LabelVisible,
        plate_visible = local.PlateVisible,
        label_y = local.LabelLocalY,
        plate_y = local.PlateTopLocalY,
        clearance = local.DepthClearance,
        text_rect = Rectangle(gpu?.ScreenRect ?? new Rect2()),
        socket_rect = Rectangle(gpu?.SocketScreenRect ?? new Rect2()),
    };

    private static float[] Rectangle(Rect2 rectangle) =>
        [rectangle.Position.X, rectangle.Position.Y, rectangle.Size.X, rectangle.Size.Y];

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) yield return descendant;
        }
    }
}
