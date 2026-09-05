// Original AnimeV1 open-ground engraving. No outer board/perimeter.
shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;

void fragment() {
    vec2 p = (UV - 0.5) * vec2(34.0, 30.0);
    float r = length(p);
    float aa = max(fwidth(r), 0.016);
    float ring = 1.0 - smoothstep(0.025, 0.025 + aa, abs(r - 7.8));
    ring += (1.0 - smoothstep(0.014, 0.014 + aa, abs(r - 8.05))) * 0.45;
    float center = 1.0 - smoothstep(0.028, 0.028 + aa, abs(p.y));
    center *= 1.0 - smoothstep(8.8, 10.2, abs(p.x));
    float territory = smoothstep(-0.5, 0.5, p.y);
    vec3 tint = mix(vec3(0.57, 0.43, 0.75), vec3(0.90, 0.78, 0.49), territory);
    ALBEDO = tint;
    ALPHA = clamp(ring * 0.27 + center * 0.40, 0.0, 0.46);
}
