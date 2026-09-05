// Original SCGS sculpted metal/enamel surface. No identity in the shared shader.
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque, specular_schlick_ggx;

uniform sampler2D artwork : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2D engraving : source_color, filter_linear_mipmap_anisotropic;
uniform vec4 art_crop = vec4(0.0, 0.0, 1.0, 1.0);
uniform vec4 name_rect = vec4(0.064,0.732,0.872,0.112);
uniform vec4 cost_rect = vec4(0.018,0.027,0.245,0.184);
uniform vec4 attack_rect = vec4(0.028,0.851,0.25,0.142);
uniform vec4 health_rect = vec4(0.722,0.851,0.25,0.142);
uniform vec4 metal_color : source_color = vec4(0.86, 0.77, 0.56, 1.0);
uniform vec4 enamel_color : source_color = vec4(0.055, 0.11, 0.22, 1.0);
uniform vec4 accent_color : source_color = vec4(0.40, 0.66, 0.95, 1.0);
uniform bool follower = true;
uniform bool evolved = false;
uniform bool anonymous_back = false;
uniform float highlight = 0.0;
uniform float energy = 0.0;

float rounded_box(vec2 p, vec2 b, float r) {
    vec2 q = abs(p) - b + r;
    return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - r;
}
float card_edge(vec2 uv) { return rounded_box(uv - vec2(0.5), vec2(0.488, 0.490), 0.040); }
float socket(vec2 uv, vec2 center, vec2 radius) {
    vec2 q = (uv - center) / radius;
    // Eight cut crystal edges rather than unrelated icon silhouettes.
    return max(max(abs(q.x), abs(q.y)), (abs(q.x) + abs(q.y)) * 0.7071) - 1.0;
}
float relief(vec2 uv) {
    float d = card_edge(uv);
    float rim = smoothstep(-0.047, -0.035, d) * (1.0 - smoothstep(-0.013, -0.001, d));
    float plaque = rounded_box(uv - name_rect.xy - name_rect.zw*0.5, name_rect.zw*0.5, 0.026);
    return rim * 0.42 + (1.0 - smoothstep(-0.007, 0.005, plaque)) * 0.28;
}
void fragment() {
    float d = card_edge(UV);
    if (d > 0.0) discard;
    if (anonymous_back) {
        // Hidden cards bind one shared back, without identity-specific frame,
        // nameplate, sockets, engraving, faction colour or evolution effects.
        vec4 back = texture(artwork, UV);
        if (back.a < 0.01) discard;
        ALBEDO = back.rgb * 0.25;
        EMISSION = back.rgb * 0.75;
        METALLIC = 0.08;
        ROUGHNESS = 0.62;
        SPECULAR = 0.24;
        NORMAL_MAP = vec3(0.5, 0.5, 1.0);
        NORMAL_MAP_DEPTH = 0.0;
    } else {
    vec3 art = texture(artwork, art_crop.xy + UV * art_crop.zw).rgb;
    vec3 detail = texture(engraving, UV).rgb;
    float rim = smoothstep(-0.048, -0.036, d);
    float rail = exp(-pow((d + 0.012) * 400.0, 2.0)) + 0.65 * exp(-pow((d + 0.041) * 520.0, 2.0));
    float plaque_d = rounded_box(UV - name_rect.xy - name_rect.zw*0.5, name_rect.zw*0.5, 0.026);
    float plaque = 1.0 - smoothstep(-0.002, 0.002, plaque_d);
    float plaque_rim = plaque * smoothstep(-0.009, -0.003, plaque_d);
    float lower = smoothstep(name_rect.y+name_rect.w, name_rect.y+name_rect.w+0.07, UV.y) * 0.45;
    vec3 base = mix(art, art * 0.58 + enamel_color.rgb * 0.26, lower);
    vec3 alloy = metal_color.rgb * (0.56 + detail * 0.48);
    // Both long rails and the nameplate use one alloy and one light direction.
    alloy *= 0.82 + 0.18 * (1.0 - UV.y) + rail * 0.40;
    base = mix(base, alloy, rim);
    vec3 plaque_color = enamel_color.rgb * (0.76 + 0.36 * (1.0 - UV.y)) + detail * 0.035;
    base = mix(base, mix(plaque_color, alloy, plaque_rim), plaque);

    float cost = socket(UV, cost_rect.xy+cost_rect.zw*0.5, cost_rect.zw*0.5);
    float attack = follower ? socket(UV, attack_rect.xy+attack_rect.zw*0.5, attack_rect.zw*0.5) : 100.0;
    float health = follower ? socket(UV, health_rect.xy+health_rect.zw*0.5, health_rect.zw*0.5) : 100.0;
    float gem = min(cost, min(attack, health));
    float gem_mask = 1.0 - smoothstep(-0.015, 0.015, gem);
    float bezel = smoothstep(-0.16, -0.09, gem);
    vec3 gem_color = vec3(0.035, 0.27, 0.30);
    vec2 center = cost_rect.xy+cost_rect.zw*0.5;
    vec2 radius = cost_rect.zw*0.5;
    if (attack < cost && attack < health) { gem_color = vec3(0.065, 0.20, 0.42); center = attack_rect.xy+attack_rect.zw*0.5; radius = attack_rect.zw*0.5; }
    if (health < cost && health < attack) { gem_color = vec3(0.42, 0.065, 0.14); center = health_rect.xy+health_rect.zw*0.5; radius = health_rect.zw*0.5; }
    vec2 g = (UV - center) / radius;
    float facets = 0.84 + 0.20 * cos(floor((atan(g.y, g.x) + 3.14159) * 1.27324) * 0.7854 - 0.7);
    vec3 crystal = gem_color * facets + vec3(0.20, 0.25, 0.28) * pow(max(0.0, -g.x * 0.45 - g.y * 0.55), 5.0);
    base = mix(base, mix(crystal, alloy * 1.15, bezel), gem_mask);

    // Small inlaid type crest: shaped metalwork, not a flat yellow sticker.
    vec2 c = (UV - vec2(0.863, 0.100)) / vec2(0.042, 0.033);
    float crest = abs(c.x) + abs(c.y);
    float crest_line = exp(-pow((crest - 0.72) * 17.0, 2.0));
    base = mix(base, alloy * 1.25, crest_line);

    float h = relief(UV);
    float dx = (relief(UV + vec2(0.001, 0.0)) - h) * 13.0;
    float dy = (relief(UV + vec2(0.0, 0.001)) - h) * 13.0;
    vec2 grain = vec2(dFdx(detail.r), dFdy(detail.r)) * rim * 0.8;
    NORMAL_MAP = normalize(vec3(-dx + grain.x, dy + grain.y, 1.0)) * 0.5 + 0.5;
    NORMAL_MAP_DEPTH = 0.8;
    float surface = max(rim, max(plaque, gem_mask));
    ALBEDO = base * mix(0.20, 0.75, surface);
    EMISSION = base * mix(0.80, 0.24, surface);
    METALLIC = max(rim, max(plaque_rim, gem_mask * bezel)) * 0.62;
    ROUGHNESS = mix(0.82, 0.30, max(rim, gem_mask));
    SPECULAR = 0.36;
    float edge_glow = exp(-pow((d + 0.007) * 600.0, 2.0));
    float pulse = 0.82 + 0.18 * sin(TIME * 3.4);
    EMISSION += accent_color.rgb * edge_glow * (highlight * pulse * 0.42 + energy * 1.5);
    if (evolved) EMISSION += accent_color.rgb * rim * 0.09;
    }
}
