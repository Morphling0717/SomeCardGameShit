// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;
using Scgs.GodotClient.Battlefield;
using Scgs.GodotClient.CardFaces;
using V05 = Scgs.Client.V05;

namespace Scgs.GodotClient.PresentationV2;

/// <summary>One shared detail viewport, not one per card. Same physical actor as the battlefield.</summary>
internal sealed partial class SculptedCardDetail : SubViewport
{
    private CardActor3D actor = null!;

    public override void _Ready()
    {
        Size = new(576, 768);
        TransparentBg = true;
        OwnWorld3D = true;
        Msaa3D = Viewport.Msaa.Msaa4X;
        RenderTargetUpdateMode = UpdateMode.Disabled;
        var camera = new Camera3D
        {
            Position = new(0, 3, 0), RotationDegrees = new(-90, 0, 0),
            Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.20f,
            Current = true, Near = .1f, Far = 6,
        };
        AddChild(camera);
        var light = new DirectionalLight3D
        {
            RotationDegrees = new(-55, -28, 0), LightColor = new("fff1d7"), LightEnergy = 1.10f,
        };
        AddChild(light);
        var environment=new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new("b9c9df"), AmbientLightEnergy = .5f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        if(CardFrameReviewRuntime.Enabled)CardFrameLighting.Apply(environment);
        AddChild(new WorldEnvironment {Environment=environment});
        actor = new CardActor3D { Name = "SharedDetailCard" };
        AddChild(actor);
        actor.ClearSensitive();
    }

    internal Texture2D Bind(V05.CardView card)
    {
        actor.BindProductFace(Battlefield3DPresenter.ComposeProductCard(card, CardFaceContext.Detail),
            Transform3D.Identity, BattlefieldCardLayout.Field);
        RenderTargetUpdateMode = UpdateMode.Always;
        return GetTexture();
    }

    internal void ClearSensitive()
    {
        actor?.ClearSensitive();
        RenderTargetClearMode = ClearMode.Always;
        RenderTargetUpdateMode = UpdateMode.Once;
    }
}
