// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.CardFaces;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>One physical layout shared by hand, field and detail. No oversized field badges.</summary>
internal static class CardFrameMaster
{
    internal const string RootPath = "res://assets/visual/anime_v1/card_frame_r1/";
    internal const float HighestSurface = .123f;
    internal const float GlyphSurface = .142f;

    internal static CardFaceComposition Compose(CardFaceComposition face)
    {
        if (!CardFrameReviewRuntime.UsesRefinedFace(face.ViewModel.DesignId)) return face;
        bool follower = face.ViewModel.Kind == ProductCardKind.Follower;
        var layout = face.Layout with {
            ArtWindow = new(.054f,.173f,.892f,.794f),
            NamePlate = new(.196f,.042f,.765f,.130f),
            NameText = new(.283f,.074f,.588f,.065f),
            CostGem = new(.025f,.019f,.228f,.212f),
            CostText = new(.049f,.046f,.180f,.158f),
            TypeCrest = new(.472f,.933f,.056f,.052f),
            AttackGem = follower ? new(.027f,.758f,.240f,.228f) : null,
            AttackText = follower ? new(.075f,.806f,.144f,.130f) : null,
            HealthGem = follower ? new(.733f,.758f,.240f,.228f) : null,
            HealthText = follower ? new(.781f,.804f,.144f,.130f) : null,
            CountdownGem = null, CountdownText = null,
        };
        layout.Validate();
        var view=face.ViewModel;
        return face with { Layout=layout, ArtCrop=CardArtCrop.Cover(view.ArtPixelWidth,
            view.ArtPixelHeight,layout.ArtWindowAspectRatio,view.ArtFocusX,view.ArtFocusY) };
    }
}
