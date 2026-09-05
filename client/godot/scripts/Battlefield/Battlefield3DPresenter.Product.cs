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
    private static readonly ICardVisualCatalog ProductBackCatalog =
        new ProductCardBackCatalog();

    internal void ConfigureProductPresentation()
    {
        SetVisualCatalog(ProductBackCatalog);
        ConfigureVisualProfile(BattlefieldVisualProfile.AnimeV1);
        if(Scgs.GodotClient.PresentationV2.CardFrameReviewRuntime.Enabled &&
            _animeArena?.GetNodeOrNull<WorldEnvironment>("Environment") is {} env)
        {
            env.Environment=(Godot.Environment)env.Environment.Duplicate();
            Scgs.GodotClient.PresentationV2.CardFrameLighting.Apply(env.Environment);
            _camera.SetCardFrameReviewFraming();
        }
        if ((Scgs.GodotClient.PresentationV2.BattlePresentationReviewRuntime.Enabled ||
             Scgs.GodotClient.PresentationV2.CardFrameReviewRuntime.Enabled) &&
            _animeArena?.GetNodeOrNull<MeshInstance3D>("PaintedVista") is { } vista)
        {
            var shader = new Shader { Code = """
                shader_type spatial;
                render_mode unshaded, cull_disabled;
                uniform sampler2D panorama : source_color, filter_linear_mipmap;
                void fragment() {
                    vec3 color = texture(panorama, UV).rgb;
                    float luma = dot(color, vec3(0.2126, 0.7152, 0.0722));
                    float center = 1.0 - smoothstep(0.18, 0.62, length((UV-0.5)*vec2(1.0,0.8)));
                    color = mix(color, vec3(luma), 0.24 + center*0.32);
                    ALBEDO = mix(color*0.74, vec3(0.14,0.15,0.20), center*0.24);
                }
                """ };
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("panorama", GD.Load<Texture2D>("res://assets/visual/anime_v1/slice/arena/open-fantasy-arena.png"));
            vista.MaterialOverride = material;
        }
    }

    internal void RenderProductPrivate(
        V05.MatchView view,
        ProductHotseatInteractionContext interaction)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(interaction);
        if (view.Players.Length != 2 || interaction.Revision != view.Revision)
        {
            throw new ArgumentException(
                "Product private battlefield input must describe one two-player revision.");
        }

        EnsureBuilt();
        // Selection and event acknowledgement do not change card content.
        // Keep the live pooled actors/hand relief instead of clearing and
        // rebinding every texture, collision and hover on the same revision.
        if (_privateRender && Revision == view.Revision &&
            PerspectiveViewer == LegacyPlayer(view.Viewer) && _handBindings.Count != 0)
        {
            return;
        }
        ResetForRender();
        Revision = view.Revision;
        PerspectiveViewer = LegacyPlayer(view.Viewer);
        _privateRender = true;
        _lastPrivateView = null;
        _lastPrivateInteraction = null;
        _lastInteractionSurfaces = [];
        _lastInteractionSelected = null;
        _lastInteractionTargetingSource = null;
        RenderProductPrivatePlayer(ProductPlayer(view, V05.PlayerId.Player0), view.Viewer);
        RenderProductPrivatePlayer(ProductPlayer(view, V05.PlayerId.Player1), view.Viewer);
        FinishRender();
        RelayoutHands(animate: false);
    }

    internal void RenderProductPublic(
        ProductHotseatPublicBoardView board,
        V05.PlayerId perspectiveViewer)
    {
        ArgumentNullException.ThrowIfNull(board);
        V04.PlayerId legacyViewer = LegacyPlayer(perspectiveViewer);
        if (board.Players.Count != 2)
        {
            throw new ArgumentException(
                "Product public battlefield input must contain two players.",
                nameof(board));
        }

        EnsureBuilt();
        ClearSensitive();
        Revision = board.Revision;
        PerspectiveViewer = legacyViewer;
        _privateRender = false;
        RenderProductPublicPlayer(ProductPlayer(board, V05.PlayerId.Player0), perspectiveViewer);
        RenderProductPublicPlayer(ProductPlayer(board, V05.PlayerId.Player1), perspectiveViewer);
        FinishRender();
        RelayoutHands(animate: false);
        SetInputEnabled(false);
    }

    private void RenderProductPrivatePlayer(V05.PlayerView player, V05.PlayerId viewer)
    {
        V04.PlayerId legacyPlayer = LegacyPlayer(player.Player);
        V04.PlayerId legacyViewer = LegacyPlayer(viewer);
        RenderProductMainBoard(player, legacyPlayer, legacyViewer);
        RenderProductTactics(player, legacyPlayer, legacyViewer);
        RenderProductField(player.Field, legacyPlayer, legacyViewer);
        RenderLeader(
            legacyPlayer,
            legacyViewer,
            player.LeaderHealth,
            player.MaximumLeaderHealth,
            interactive: true);

        int handCount = player.Player == viewer
            ? Math.Min(player.Hand.Length, BattlefieldPerspective.MaximumHandCards)
            : Math.Min(CheckedDisplayCount(player.HandCount), BattlefieldPerspective.MaximumHandCards);
        for (int index = 0; index < handCount; ++index)
        {
            CardActor3D actor = RentCard();
            HandCardPose pose = _handRig.CreatePose(
                legacyPlayer,
                legacyViewer,
                index,
                handCount);
            if (player.Player == viewer && index < player.Hand.Length &&
                ProductIdentityKnown(player.Hand[index]))
            {
                V05.CardView card = player.Hand[index];
                BattlefieldSurfaceRef surface = new(
                    BattlefieldSurfaceKind.HandCard,
                    legacyPlayer,
                    index,
                    card.InstanceId);
                actor.BindProductFace(
                    ComposeProductCard(card, CardFaceContext.Hand),
                    pose.Transform,
                    BattlefieldCardLayout.NearHand,
                    surface);
                Register(surface, actor);
            }
            else
            {
                actor.BindHidden(
                    legacyPlayer,
                    V04.Zone.Hand,
                    pose.Transform,
                    pose.Near
                        ? BattlefieldCardLayout.NearHand
                        : BattlefieldCardLayout.FarHand);
            }
            _handBindings.Add(new HandActorBinding(
                actor,
                legacyPlayer,
                index,
                handCount,
                pose.Near));
        }

        BattlefieldSurfaceRef standbySurface = BattlefieldSurfaceRef.StandbyPile(legacyPlayer);
        CardActor3D standby = RentCard();
        standby.BindPile(
            legacyPlayer,
            V04.Zone.Standby,
            "战备",
            (ulong)player.Standby.Length,
            BattlefieldPerspective.StandbyPileTransform(legacyPlayer, legacyViewer),
            hidden: true,
            standbySurface);
        Register(standbySurface, standby);
        RenderPile(legacyPlayer, legacyViewer, V04.Zone.Deck, "牌组", player.DeckCount, hidden: true);
        RenderProductOpenPile(legacyPlayer, legacyViewer, V04.Zone.Graveyard, "墓地", player.Graveyard.Length);
        RenderProductOpenPile(legacyPlayer, legacyViewer, V04.Zone.Archive, "封存", player.Archive.Length);
    }

    private void RenderProductMainBoard(
        V05.PlayerView player,
        V04.PlayerId legacyPlayer,
        V04.PlayerId legacyViewer)
    {
        for (int slot = 0; slot < BattlefieldPerspective.UnitSlotCount; ++slot)
        {
            Transform3D transform = BattlefieldPerspective.UnitTransform(
                legacyPlayer,
                legacyViewer,
                slot);
            BattlefieldSurfaceRef slotSurface = new(
                BattlefieldSurfaceKind.UnitSlot,
                legacyPlayer,
                slot);
            SlotActor3D slotActor = RentSlot();
            slotActor.Bind(transform, "主战场", slotSurface);
            Register(slotSurface, slotActor);
            V05.CardView? card = slot < player.MainBoard.Length ? player.MainBoard[slot] : null;
            if (card is null)
            {
                continue;
            }
            BindProductPermanent(
                card,
                new BattlefieldSurfaceRef(
                    BattlefieldSurfaceKind.Unit,
                    legacyPlayer,
                    slot,
                    card.InstanceId),
                transform);
        }
    }

    private void RenderProductTactics(
        V05.PlayerView player,
        V04.PlayerId legacyPlayer,
        V04.PlayerId legacyViewer)
    {
        for (int slot = 0; slot < BattlefieldPerspective.TacticSlotCount; ++slot)
        {
            Transform3D transform = BattlefieldPerspective.TacticTransform(
                legacyPlayer,
                legacyViewer,
                slot);
            BattlefieldSurfaceRef slotSurface = new(
                BattlefieldSurfaceKind.TacticSlot,
                legacyPlayer,
                slot);
            SlotActor3D slotActor = RentSlot();
            slotActor.Bind(transform, "策略", slotSurface);
            Register(slotSurface, slotActor);
            V05.CardView? card = slot < player.Tactics.Length ? player.Tactics[slot] : null;
            if (card is null)
            {
                continue;
            }

            BattlefieldSurfaceRef surface = new(
                BattlefieldSurfaceKind.Tactic,
                legacyPlayer,
                slot,
                card.InstanceId);
            if (ProductIdentityKnown(card) && !card.FaceDown)
            {
                BindProductPermanent(card, surface, transform);
            }
            else if (ProductIdentityKnown(card) && card.Controller == player.Player)
            {
                // The owner may inspect their own armed trap; the product
                // frame is viewer-safe because the DTO disclosed its identity.
                BindProductPermanent(card, surface, transform);
            }
            else
            {
                CardActor3D actor = RentCard();
                actor.BindHidden(
                    legacyPlayer,
                    V04.Zone.Tactic,
                    transform,
                    BattlefieldCardLayout.Field,
                    surface);
                Register(surface, actor);
            }
        }
    }

    private void RenderProductField(
        V05.CardView? card,
        V04.PlayerId player,
        V04.PlayerId viewer)
    {
        Transform3D transform = BattlefieldPerspective.ProductFieldTransform(player, viewer);
        BattlefieldSurfaceRef slot = new(BattlefieldSurfaceKind.FieldSlot, player);
        SlotActor3D slotActor = RentSlot();
        slotActor.Bind(transform, "场地", slot);
        Register(slot, slotActor);
        if (card is not null)
        {
            BindProductPermanent(
                card,
                new BattlefieldSurfaceRef(
                    BattlefieldSurfaceKind.FieldCard,
                    player,
                    InstanceId: card.InstanceId),
                transform);
        }
    }

    private void BindProductPermanent(
        V05.CardView card,
        BattlefieldSurfaceRef surface,
        Transform3D transform)
    {
        CardActor3D actor = RentCard();
        if (ProductIdentityKnown(card) && !card.FaceDown)
        {
            actor.BindProductFace(
                ComposeProductCard(card, CardFaceContext.Field),
                transform,
                BattlefieldCardLayout.Field,
                surface);
        }
        else
        {
            actor.BindHidden(
                LegacyPlayer(card.Controller),
                LegacyZone(card.Zone),
                transform,
                BattlefieldCardLayout.Field,
                surface);
        }
        Register(surface, actor);
    }

    private void RenderProductPublicPlayer(
        ProductHotseatPublicPlayerView player,
        V05.PlayerId viewer)
    {
        V04.PlayerId legacyPlayer = LegacyPlayer(player.Player);
        V04.PlayerId legacyViewer = LegacyPlayer(viewer);
        RenderProductPublicSlots(player.MainBoard, legacyPlayer, legacyViewer, mainBoard: true);
        RenderProductPublicSlots(player.Tactics, legacyPlayer, legacyViewer, mainBoard: false);
        RenderProductPublicField(player.Field, legacyPlayer, legacyViewer);
        RenderLeader(
            legacyPlayer,
            legacyViewer,
            player.LeaderHealth,
            player.MaximumLeaderHealth,
            interactive: false);

        int handCount = Math.Min(
            CheckedDisplayCount(player.HandCount),
            BattlefieldPerspective.MaximumHandCards);
        for (int index = 0; index < handCount; ++index)
        {
            CardActor3D actor = RentCard();
            HandCardPose pose = _handRig.CreatePose(
                legacyPlayer,
                legacyViewer,
                index,
                handCount);
            actor.BindHidden(
                legacyPlayer,
                V04.Zone.Hand,
                pose.Transform,
                pose.Near ? BattlefieldCardLayout.NearHand : BattlefieldCardLayout.FarHand);
            _handBindings.Add(new HandActorBinding(
                actor,
                legacyPlayer,
                index,
                handCount,
                pose.Near));
        }

        RentCard().BindPile(
            legacyPlayer,
            V04.Zone.Standby,
            "战备",
            (ulong)player.Standby.Count,
            BattlefieldPerspective.StandbyPileTransform(legacyPlayer, legacyViewer),
            hidden: true);
        RenderPile(legacyPlayer, legacyViewer, V04.Zone.Deck, "牌组", player.DeckCount, hidden: true);
        RenderProductOpenPile(legacyPlayer, legacyViewer, V04.Zone.Graveyard, "墓地", player.Graveyard.Count);
        RenderProductOpenPile(legacyPlayer, legacyViewer, V04.Zone.Archive, "封存", player.Archive.Count);
    }

    private void RenderProductPublicSlots(
        IReadOnlyList<ProductHotseatPublicCardView?> cards,
        V04.PlayerId player,
        V04.PlayerId viewer,
        bool mainBoard)
    {
        int count = mainBoard
            ? BattlefieldPerspective.UnitSlotCount
            : BattlefieldPerspective.TacticSlotCount;
        for (int slot = 0; slot < count; ++slot)
        {
            Transform3D transform = mainBoard
                ? BattlefieldPerspective.UnitTransform(player, viewer, slot)
                : BattlefieldPerspective.TacticTransform(player, viewer, slot);
            RentSlot().Bind(transform, mainBoard ? "主战场" : "策略", surface: null);
            ProductHotseatPublicCardView? card = slot < cards.Count ? cards[slot] : null;
            if (card is null)
            {
                continue;
            }
            CardActor3D actor = RentCard();
            if (card.HasKnownIdentity && !card.FaceDown)
            {
                presentationOriginals[card.InstanceId!.Value] = actor;
                actor.BindProductFace(
                    ComposeProductCard(card, CardFaceContext.Field),
                    transform,
                    BattlefieldCardLayout.Field);
            }
            else
            {
                actor.BindHidden(
                    player,
                    mainBoard ? V04.Zone.Unit : V04.Zone.Tactic,
                    transform,
                    BattlefieldCardLayout.Field);
            }
        }
    }

    private void RenderProductPublicField(
        ProductHotseatPublicCardView? card,
        V04.PlayerId player,
        V04.PlayerId viewer)
    {
        Transform3D transform = BattlefieldPerspective.ProductFieldTransform(player, viewer);
        RentSlot().Bind(transform, "场地", surface: null);
        if (card is null)
        {
            return;
        }
        CardActor3D actor = RentCard();
        if (card.HasKnownIdentity && !card.FaceDown)
        {
            presentationOriginals[card.InstanceId!.Value] = actor;
            actor.BindProductFace(
                ComposeProductCard(card, CardFaceContext.Field),
                transform,
                BattlefieldCardLayout.Field);
        }
        else
        {
            actor.BindHidden(player, V04.Zone.Unit, transform, BattlefieldCardLayout.Field);
        }
    }

    private void RenderProductOpenPile(
        V04.PlayerId player,
        V04.PlayerId viewer,
        V04.Zone zone,
        string label,
        int count)
    {
        if (count == 0)
        {
            RenderEmptyPile(player, viewer, zone, label);
            return;
        }
        RentCard().BindPile(
            player,
            zone,
            label,
            (ulong)count,
            BattlefieldPerspective.PileTransform(player, viewer, zone),
            hidden: false);
    }

    internal static CardFaceComposition ComposeProductCard(
        V05.CardView card,
        CardFaceContext context)
    {
        if (!ProductIdentityKnown(card))
        {
            throw new ArgumentException("A product card face requires viewer-safe identity.", nameof(card));
        }
        ProductCardVisualEntry visual = ProductCardVisualCatalog.Shared.Resolve(card.DesignId!);
        return ComposeProductCard(
            card.DesignId!,
            card.Name,
            visual,
            card.Cost,
            card.CurrentAttack,
            card.CurrentHealth,
            card.Countdown,
            card.Evolved,
            context);
    }

    private static CardFaceComposition ComposeProductCard(
        ProductHotseatPublicCardView card,
        CardFaceContext context)
    {
        if (!card.HasKnownIdentity)
        {
            throw new ArgumentException("A public product card face requires known identity.", nameof(card));
        }
        ProductCardVisualEntry visual = ProductCardVisualCatalog.Shared.Resolve(card.DesignId!);
        return ComposeProductCard(
            card.DesignId!,
            card.Name,
            visual,
            card.Cost,
            card.CurrentAttack,
            card.CurrentHealth,
            card.Countdown,
            card.Evolved,
            context);
    }

    private static CardFaceComposition ComposeProductCard(
        string designId,
        string displayName,
        ProductCardVisualEntry visual,
        int cost,
        int attack,
        int health,
        int countdown,
        bool evolved,
        CardFaceContext context)
    {
        var model = new CardFaceViewModel
        {
            DesignId = designId,
            DisplayName = displayName,
            Kind = visual.Kind,
            Faction = visual.Faction,
            Rarity = visual.Rarity,
            Cost = cost,
            Attack = visual.Kind == ProductCardKind.Follower ? attack : null,
            Health = visual.Kind == ProductCardKind.Follower ? health : null,
            Countdown = visual.Kind is ProductCardKind.Amulet or ProductCardKind.Trap
                ? countdown
                : null,
            Variant = designId == "LO-T01"
                ? CardFrameVariant.Token
                : evolved
                    ? CardFrameVariant.Evolved
                    : CardFrameVariant.Normal,
        };
        return CardFaceComposer.Compose(
            model,
            context,
            ProductCardVisualCatalog.Shared,
            CardFrameStyleCatalog.Shared);
    }

    private static bool ProductIdentityKnown(V05.CardView card) =>
        card.InstanceId.HasValue && !string.IsNullOrWhiteSpace(card.DesignId) &&
        card.Kind.HasValue;

    private static V05.PlayerView ProductPlayer(V05.MatchView view, V05.PlayerId player) =>
        view.Players.Single(candidate => candidate.Player == player);

    private static ProductHotseatPublicPlayerView ProductPlayer(
        ProductHotseatPublicBoardView view,
        V05.PlayerId player) => view.Players.Single(candidate => candidate.Player == player);

    internal static V04.PlayerId LegacyPlayer(V05.PlayerId player) => player switch
    {
        V05.PlayerId.Player0 => V04.PlayerId.Player0,
        V05.PlayerId.Player1 => V04.PlayerId.Player1,
        _ => throw new ArgumentOutOfRangeException(nameof(player), player, "Unknown product player."),
    };

    private static V04.Zone LegacyZone(V05.Zone zone) => zone switch
    {
        V05.Zone.Deck => V04.Zone.Deck,
        V05.Zone.Hand => V04.Zone.Hand,
        V05.Zone.MainBoard or V05.Zone.Field => V04.Zone.Unit,
        V05.Zone.Tactic => V04.Zone.Tactic,
        V05.Zone.Graveyard => V04.Zone.Graveyard,
        V05.Zone.Archive => V04.Zone.Archive,
        V05.Zone.Standby => V04.Zone.Standby,
        _ => V04.Zone.None,
    };

    private sealed class ProductCardBackCatalog : ICardVisualCatalog
    {
        private readonly Texture2D cardBack = Load(
            ProductCardVisualCatalog.SharedCardBack,
            "AnimeV1 card back");
        private readonly Texture2D fallback = Load(
            ProductCardVisualCatalog.FallbackArt,
            "product fallback face");

        public IReadOnlyCollection<CardVisualEntry> Entries => Array.Empty<CardVisualEntry>();
        public Texture2D CardBack => cardBack;
        public Texture2D FallbackFront => fallback;
        public CardVisualEntry? Find(uint definitionId) => null;
        public Texture2D LoadArtwork(uint definitionId) => fallback;

        private static Texture2D Load(string path, string label) =>
            ResourceLoader.Exists(path, "Texture2D")
                ? GD.Load<Texture2D>(path)
                : throw new InvalidOperationException($"Missing {label}: {path}");
    }
}
