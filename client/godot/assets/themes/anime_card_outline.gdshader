shader_type spatial;
render_mode unshaded, cull_disabled, depth_draw_never;
uniform vec4 accent : source_color = vec4(0.90, 0.79, 0.53, 1.0);
void fragment() {
    vec2 p = abs(UV - 0.5);
    float edge = max(p.x, p.y);
    float line = smoothstep(0.477, 0.487, edge);
    ALBEDO = accent.rgb;
    ALPHA = accent.a * line;
}
