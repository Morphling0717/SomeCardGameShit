// SPDX-License-Identifier: GPL-3.0-or-later
using Scgs.GodotClient.CardFaces;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>Explicit approval lane. Never silently replaces the released product skin.</summary>
internal static class BattlePresentationReviewRuntime
{
    internal static bool Enabled { get; private set; }
    internal static void Configure(bool enabled) => Enabled = enabled;
    internal static bool UsesSculptedFace(string designId) => Enabled &&
        designId is "LO-11" or "AP-11" or "NT-04";

    internal static CardFaceComposition Compose(CardFaceComposition face)
    {
        if (!UsesSculptedFace(face.ViewModel.DesignId)) return face;
        bool follower = face.ViewModel.Kind == ProductCardKind.Follower;
        var layout = face.Layout with
        {
            NamePlate = new(0.064f, 0.732f, 0.872f, 0.112f),
            NameText = new(0.140f, 0.748f, 0.720f, 0.079f),
            CostGem = new(0.018f, 0.027f, 0.245f, 0.184f),
            CostText = new(0.047f, 0.049f, 0.186f, 0.134f),
            TypeCrest = new(0.814f, 0.061f, 0.098f, 0.075f),
            AttackGem = follower ? new(0.028f, 0.851f, 0.250f, 0.142f) : null,
            AttackText = follower ? new(0.067f, 0.860f, 0.170f, 0.125f) : null,
            HealthGem = follower ? new(0.722f, 0.851f, 0.250f, 0.142f) : null,
            HealthText = follower ? new(0.762f, 0.860f, 0.170f, 0.125f) : null,
        };
        if (face.Layout.Context == CardFaceContext.Field)
        {
            // Foreshortened battlefield cards need larger information bays,
            // not tiny versions of the readable close-up face.
            layout = layout with {
                CostGem = new(.008f,.008f,.395f,.325f),
                CostText = new(.057f,.037f,.296f,.278f),
                NamePlate = new(.064f,.565f,.872f,.140f),
                NameText = new(.140f,.590f,.720f,.090f),
                AttackGem = follower ? new(.007f,.714f,.420f,.282f) : null,
                AttackText = follower ? new(.072f,.729f,.290f,.252f) : null,
                HealthGem = follower ? new(.573f,.714f,.420f,.282f) : null,
                HealthText = follower ? new(.638f,.729f,.290f,.252f) : null,
            };
        }
        layout.Validate();
        return face with { Layout = layout };
    }
}
