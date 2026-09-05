// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.Ci;
using Scgs.Hotseat.Product;

namespace Scgs.GodotClient.Match;

public sealed partial class ProductMatchScreen
{
    private ProductPrivacyProbe? ciPrivacyProbe;

    internal bool CiProductPrivacyVerificationPending => ciPrivacyProbe?.VerificationPending == true;

    internal void CiAttachProductPrivacy(ProductPrivacyProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);
        if (ciPrivacyProbe is not null && !ReferenceEquals(ciPrivacyProbe, probe))
            throw new InvalidOperationException("Product privacy evidence owner cannot change.");
        if (!CiAudit.IsRealNativeSession) throw new InvalidOperationException("Product privacy requires a real v05 session.");
        ciPrivacyProbe = probe;
    }

    // Production/non-smoke calls do nothing. First actual mulligan only: its
    // hand was already revealed by the real button, and it will normally hand off.
    internal Task CiArmProductPrivacyBeforePrepareAsync()
    {
        if (ciPrivacyProbe is null || ciPrivacyProbe.IsArmed || controller?.State is not
            { Mode: ProductHotseatUiMode.MulliganSelecting, Snapshot: { } snapshot, Viewer: { } viewer, CanPrepare: true })
            return Task.CompletedTask;
        ulong revision = snapshot.Revision;
        ulong generation = sessionGeneration;
        CardActor3D actor = CiPrivacyNodes(battlefield).OfType<CardActor3D>().FirstOrDefault(card =>
            card.IsVisibleInTree() && card.CiProductFace is not null &&
            card.CiLayout == BattlefieldCardLayout.NearHand && card.Surface is
                { Kind: BattlefieldSurfaceKind.HandCard, InstanceId: not null } surface &&
            surface.Player == Battlefield3DPresenter.LegacyPlayer(viewer)) ??
            throw new InvalidOperationException("No actual revealed product hand actor for the privacy probe.");
        return ciPrivacyProbe.ArmAsync(
            () =>
            {
                actor.CiArmPrivacySentinel(ProductPrivacyProbe.Sentinel);
                battlefield.CiRaycastInput.CiArmDragToken(actor, revision);
            },
            () => !leavingScene && IsInsideTree() && generation == sessionGeneration &&
                controller?.State is { Mode: ProductHotseatUiMode.MulliganSelecting, Snapshot: { } current } state &&
                state.Viewer == viewer && current.Revision == revision,
            () => GodotObject.IsInstanceValid(actor) &&
                actor.CiHasPrivacyTextureSentinel(ProductPrivacyProbe.Sentinel) &&
                actor.CountForbiddenToken(ProductPrivacyProbe.Sentinel) > 0 && battlefield.CiHasActiveDrag);
    }

    internal Task CiObserveProductPrivacyAsync() =>
        ciPrivacyProbe?.ObserveAsync(CiPrivacyObservation) ?? Task.CompletedTask;

    private ProductPrivacyObservation? CiPrivacyObservation()
    {
        if (leavingScene || controller is null || ciSession is null || !IsInsideTree()) return null;
        ProductHotseatUiState state = controller.State;
        string? phase = state.Mode switch
        {
            ProductHotseatUiMode.Resolving => "resolving",
            ProductHotseatUiMode.Covered => "covered",
            _ => null,
        };
        if (phase is null) return null;
        CiObserveSafeFrame();
        int tokens = 0, privateResources = 0, privateCallbacks = 0, hiddenIdentities = 0;
        var visitedResources = new HashSet<ulong>();
        foreach (Node node in CiPrivacyNodes(this))
        {
            if (node is CardActor3D card)
            {
                tokens += card.CountForbiddenToken(ProductPrivacyProbe.Sentinel);
                if (card.CiHasPrivacyResources) ++privateResources;
                if (card.CiHasActiveHoverTween) ++privateCallbacks;
                if (card.CiAnonymousFaceHasIdentity) ++hiddenIdentities;
                if (card.CardPresentation is { FaceDown: true } && card.Visible && !card.CiUsesSharedCardBack) ++hiddenIdentities;
            }
            if (node is SlotActor3D slot) tokens += slot.CountForbiddenToken(ProductPrivacyProbe.Sentinel);
            CheckText(node.Name);
            if (node is Label3D label3d) CheckText(label3d.Text);
            if (node is Label label) CheckText(label.Text);
            if (node is RichTextLabel rich) CheckText(rich.Text);
            if (node is Button button) CheckText(button.Text);
            if (node is Control control) CheckText(control.TooltipText);
            foreach (StringName key in node.GetMetaList()) { CheckText(key); CheckText(node.GetMeta(key).ToString()); }
            if (node is GeometryInstance3D geometry) { VisitMaterial(geometry.MaterialOverride); VisitMaterial(geometry.MaterialOverlay); }
            if (node is MeshInstance3D mesh && mesh.Mesh is { } source)
                for (int surface = 0; surface < source.GetSurfaceCount(); ++surface)
                { VisitMaterial(source.SurfaceGetMaterial(surface)); VisitMaterial(mesh.GetSurfaceOverrideMaterial(surface)); }
            if (node is CanvasItem canvas) VisitMaterial(canvas.Material);
            if (node is TextureRect texture) VisitTexture(texture.Texture);
            if (node is Sprite2D sprite) VisitTexture(sprite.Texture);
            if (node is Sprite3D sprite3d) VisitTexture(sprite3d.Texture);
        }
        return new ProductPrivacyObservation(phase, state.Viewer is { } viewer ? (int)viewer : null,
            state.PublicBoard?.Revision ?? ciSession.Revision, ciSession.ViewerReadCount, ciSession.PrivateQueryCount,
            tokens, privateResources, battlefield.CiCollisionEnabledCount,
            battlefield.CiHasActiveDrag ? 1 : 0, privateCallbacks, hiddenIdentities,
            battlefield.InputEnabled || battlefield.CiStableSurfaceLookupCount != 0, CiPrivacyCoverIsActuallyOpaque());

        void CheckText(string? text)
        {
            if (text?.Contains(ProductPrivacyProbe.Sentinel, StringComparison.Ordinal) == true) ++tokens;
        }
        void VisitTexture(Texture2D? texture)
        {
            if (texture is null || !visitedResources.Add(texture.GetInstanceId())) return;
            if (texture.ResourceName.Contains(ProductPrivacyProbe.Sentinel, StringComparison.Ordinal) ||
                texture.ResourcePath.Contains(ProductPrivacyProbe.Sentinel, StringComparison.Ordinal)) ++privateResources;
            if (texture is AtlasTexture atlas) VisitTexture(atlas.Atlas);
        }
        void VisitMaterial(Material? material)
        {
            if (material is null || !visitedResources.Add(material.GetInstanceId())) return;
            if (material.ResourceName.Contains(ProductPrivacyProbe.Sentinel, StringComparison.Ordinal)) ++privateResources;
            VisitMaterial(material.NextPass);
            if (material is BaseMaterial3D standard)
            { VisitTexture(standard.AlbedoTexture); VisitTexture(standard.NormalTexture); VisitTexture(standard.EmissionTexture); VisitTexture(standard.OrmTexture); }
            if (material is ShaderMaterial shader && shader.Shader is { } source)
                foreach (Godot.Collections.Dictionary uniform in source.GetShaderUniformList())
                {
                    Variant value = shader.GetShaderParameter(uniform["name"].AsStringName());
                    if (value.VariantType == Variant.Type.Object && value.AsGodotObject() is Texture2D texture) VisitTexture(texture);
                    else if (value.VariantType == Variant.Type.String) CheckText(value.AsString());
                }
        }
    }

    private bool CiPrivacyCoverIsActuallyOpaque()
    {
        // IsCovering alone only means Visible. Validate the actual full-viewport
        // raster layer and its entire alpha chain, not the intended UI mode.
        if (!privacy.IsVisibleInTree() || privacy.GetNodeOrNull<TextureRect>("OpaqueBackground") is not { } background ||
            !background.IsVisibleInTree() || background.Material is not null || background.UseParentMaterial ||
            background.Texture is not GradientTexture2D { Gradient: { } gradient } ||
            gradient.Colors.Length < 2 || gradient.Colors.Any(color => color.A < 0.999f)) return false;
        for (Node? node = background; node is not null; node = node.GetParent())
            if (node is CanvasItem canvas && (canvas.Modulate.A < 0.999f || canvas.SelfModulate.A < 0.999f)) return false;
        Rect2 viewport = GetViewport().GetVisibleRect();
        Rect2 cover = background.GetGlobalRect();
        return cover.Position.X <= viewport.Position.X + 0.01f && cover.Position.Y <= viewport.Position.Y + 0.01f &&
            cover.End.X >= viewport.End.X - 0.01f && cover.End.Y >= viewport.End.Y - 0.01f;
    }

    private static IEnumerable<Node> CiPrivacyNodes(Node node)
    {
        yield return node;
        foreach (Node child in node.GetChildren())
            foreach (Node descendant in CiPrivacyNodes(child)) yield return descendant;
    }
}
