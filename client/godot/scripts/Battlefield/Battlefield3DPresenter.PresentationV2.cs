// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.CardFaces;
using Scgs.GodotClient.Visuals;
using Scgs.Hotseat.Product;
using V04 = Scgs.Client;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.Battlefield;

public sealed partial class Battlefield3DPresenter
{
    // Only identities already public at occurrence time enter these maps.
    private readonly Dictionary<ulong, CardActor3D> presentationOriginals = new();
    private readonly Dictionary<ulong, V05.EventObservationState> presentationStates = new();
    private readonly List<CardActor3D> presentationPool = new();
    private int presentationCursor;

    internal bool TryPresentationTransform(ProductPresentationBatch batch,
        V05.EventObservationEndpoint? endpoint, V05.EventObservationLocation? location,
        out Transform3D worldPose)
    {
        worldPose = Transform3D.Identity;
        if (endpoint is { Hidden: true }) return false;
        V04.PlayerId viewer = LegacyPlayer(batch.PerspectivePlayer);
        V04.PlayerId player = LegacyPlayer(location?.Player ?? endpoint?.Player ?? batch.PerspectivePlayer);
        Transform3D local;
        if (location is not null)
        {
            local = location.Zone switch
            {
                V05.Zone.MainBoard when location.Slot is < 5 => BattlefieldPerspective.UnitTransform(player, viewer, (int)location.Slot.Value),
                V05.Zone.Tactic when location.Slot is < 3 => BattlefieldPerspective.TacticTransform(player, viewer, (int)location.Slot.Value),
                V05.Zone.Field => BattlefieldPerspective.ProductFieldTransform(player, viewer),
                V05.Zone.Hand => _handRig.CreatePose(player, viewer, 0, 1).Transform,
                V05.Zone.Standby => BattlefieldPerspective.StandbyPileTransform(player, viewer),
                V05.Zone.Graveyard => BattlefieldPerspective.PileTransform(player, viewer, V04.Zone.Graveyard),
                V05.Zone.Archive => BattlefieldPerspective.PileTransform(player, viewer, V04.Zone.Archive),
                V05.Zone.Deck => BattlefieldPerspective.PileTransform(player, viewer, V04.Zone.Deck),
                _ => Transform3D.Identity,
            };
            if (local == Transform3D.Identity) return false;
        }
        else if (endpoint?.Kind == "leader")
            local = BattlefieldPerspective.LeaderTransform(player, viewer);
        else if (endpoint?.Card is { } id && presentationOriginals.TryGetValue(id, out CardActor3D? actor))
        {
            worldPose = actor.GlobalTransform;
            return true;
        }
        else return false;
        if(Scgs.GodotClient.PresentationV2.CardFrameReviewRuntime.UsesRefinedFace(endpoint?.DesignId) &&
            location?.Zone is V05.Zone.MainBoard or V05.Zone.Tactic or V05.Zone.Field)
            local=local.ScaledLocal(Vector3.One*1.16f);
        worldPose = GlobalTransform * local;
        return true;
    }

    internal CardActor3D? RentPresentationCard(ProductPresentationBatch batch,
        V05.EventObservationEndpoint subject, V05.EventObservationState? state)
    {
        if (subject.Hidden || subject.Card is null || subject.DesignId is null) return null;
        CardFaceComposition? face = PresentationFace(batch, subject, state);
        if (face is null || presentationCursor >= 64) return null;
        if (presentationCursor == presentationPool.Count)
        {
            var next = new CardActor3D { Name = $"PublicMotion{presentationPool.Count}" };
            AddChild(next);
            presentationPool.Add(next);
        }
        CardActor3D actor = presentationPool[presentationCursor++];
        actor.BindProductFace(face, Transform3D.Identity, BattlefieldCardLayout.Field);
        return actor;
    }

    internal void SetPresentationOriginalVisible(ulong instanceId, bool visible)
    {
        if (presentationOriginals.TryGetValue(instanceId, out CardActor3D? actor)) actor.Visible = visible;
    }

    internal void ApplyPresentationObservation(ProductPresentationBatch batch, V05.ProductEventObservation observation)
    {
        if (!observation.PublicToAll || observation.Version != 1 ||
            observation.Subject is not { Hidden: false, Card: { } id, DesignId: not null } subject) return;
        if (observation.After is { } after)
        {
            presentationStates.TryGetValue(id, out V05.EventObservationState? prior);
            presentationStates[id] = new V05.EventObservationState {
                Attack = after.Attack ?? prior?.Attack, Health = after.Health ?? prior?.Health,
                MaxHealth = after.MaxHealth ?? prior?.MaxHealth, Countdown = after.Countdown ?? prior?.Countdown,
                Evolved = after.Evolved ?? prior?.Evolved, Keywords = after.Keywords ?? prior?.Keywords,
            };
        }
        if (observation.Kind == "move" && observation.To?.Zone is not
            (V05.Zone.MainBoard or V05.Zone.Tactic or V05.Zone.Field))
        {
            if (presentationOriginals.Remove(id, out CardActor3D? departing)) departing.ClearSensitive();
            return;
        }
        if (!TryPresentationTransform(batch, subject, observation.Kind == "move" ? observation.To : null, out Transform3D pose)) return;
        CardFaceComposition? face = PresentationFace(batch, subject, observation.After);
        if (face is null) return;
        if (!presentationOriginals.TryGetValue(id, out CardActor3D? actor))
        {
            actor = RentCard();
            presentationOriginals[id] = actor;
        }
        actor.BindProductFace(face, GlobalTransform.AffineInverse() * pose, BattlefieldCardLayout.Field,
            reviewPoseAlreadyScaled:true);
    }

    internal void ClearPresentationActors()
    {
        foreach (CardActor3D actor in presentationOriginals.Values) actor.Visible = true;
        foreach (CardActor3D actor in presentationPool) actor.ClearSensitive();
        presentationCursor = 0;
    }

    private CardFaceComposition? PresentationFace(ProductPresentationBatch batch,
        V05.EventObservationEndpoint subject, V05.EventObservationState? explicitState)
    {
        if (subject.Hidden || subject.Card is not { } id || subject.DesignId is not { } design) return null;
        ProductHotseatPublicCardView? card = PublicCards(batch.Before).Concat(PublicCards(batch.After))
            .FirstOrDefault(candidate => candidate.HasKnownIdentity && !candidate.FaceDown &&
                candidate.InstanceId == id && candidate.DesignId == design);
        if (card is null) return null; // No reconstruction of undisclosed identity.
        V05.EventObservationState? state = explicitState;
        if (state is null) presentationStates.TryGetValue(id, out state);
        return ComposeProductCard(design, card.Name, ProductCardVisualCatalog.Shared.Resolve(design),
            card.Cost, state?.Attack ?? card.CurrentAttack, state?.Health ?? card.CurrentHealth,
            state?.Countdown ?? card.Countdown, state?.Evolved ?? card.Evolved, CardFaceContext.Field);
    }

    private static IEnumerable<ProductHotseatPublicCardView> PublicCards(ProductHotseatPublicBoardView board) =>
        board.Players.SelectMany(player => player.MainBoard.Concat(player.Tactics)
            .Append(player.Field).Concat(player.Graveyard).Concat(player.Archive).Concat(player.Standby))
            .OfType<ProductHotseatPublicCardView>();
}
