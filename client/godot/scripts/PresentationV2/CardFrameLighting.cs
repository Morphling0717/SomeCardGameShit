// SPDX-License-Identifier: GPL-3.0-or-later
using Godot;

namespace Scgs.GodotClient.PresentationV2;

internal static class CardFrameLighting
{
    // A shared, stationary studio reflection environment. No frame emission or
    // scrolling foil, and no render-target allocation per card.
    private static readonly Sky StudioSky = new() {
        RadianceSize=Sky.RadianceSizeEnum.Size256,
        SkyMaterial=new ShaderMaterial {Shader=new Shader {Code="""
            shader_type sky;
            void sky() {
                vec3 d=normalize(EYEDIR);
                vec3 base=mix(vec3(.06,.065,.10),vec3(.44,.50,.62),smoothstep(-.30,.90,d.y));
                float key=smoothstep(.86,.92,dot(d,normalize(vec3(-.65,.90,.55))));
                float strip=smoothstep(.960,.980,dot(d,normalize(vec3(.65,.3,-.7))));
                COLOR=base + key*vec3(1.9,1.7,1.45)+strip*vec3(.48,.58,.85);
            }
            """}}
    };
    internal static void Apply(Godot.Environment environment)
    {
        environment.Sky=StudioSky;
        environment.ReflectedLightSource=Godot.Environment.ReflectionSource.Sky;
    }
}
