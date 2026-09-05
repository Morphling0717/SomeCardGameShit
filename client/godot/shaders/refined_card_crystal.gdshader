// SPDX-License-Identifier: GPL-3.0-or-later
shader_type spatial;
render_mode cull_disabled;

// Opaque jewel optics. Facet normals come from the GLB, not an albedo noise
// derivative. A quiet interior absorption/caustic tint avoids flat enamel-like
// tables; no screen refraction, transparency stack, emission or animation.
uniform vec3 tint : source_color = vec3(.02,.25,.12);
uniform vec4 jewel_uv = vec4(.139,.125,.104,.100);

void fragment() {
    vec2 q=(UV-jewel_uv.xy)/jewel_uv.zw;
    float radial=clamp(dot(q,q),0.,1.5);
    float depth=clamp(.24+.57*(1.-radial)+.32*q.y-.23*q.x,.13,1.);
    float inner_light=pow(clamp(1.-abs(q.x*.78+q.y*.46-.32),0.,1.),28.);
    ALBEDO=tint*depth + tint*inner_light*.40;
    METALLIC=.30;
    ROUGHNESS=.115;
    SPECULAR=.95;
}
