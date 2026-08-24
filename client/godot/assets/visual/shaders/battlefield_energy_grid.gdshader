shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded;

uniform vec4 base_color : source_color = vec4(0.012, 0.035, 0.060, 0.88);
uniform vec4 grid_color : source_color = vec4(0.050, 0.310, 0.380, 0.22);
uniform vec4 far_color : source_color = vec4(0.190, 0.070, 0.310, 0.15);
uniform float grid_scale = 15.0;

float energy_line(float coordinate) {
    float distance_to_cell = abs(fract(coordinate) - 0.5);
    return smoothstep(0.455, 0.495, distance_to_cell);
}

void fragment() {
    vec2 centered = UV - vec2(0.5);
    vec2 cell = centered * vec2(1.55, 1.0) * grid_scale;

    // Three sixty-degree line families make a restrained hex/energy lattice.
    float vertical = energy_line(cell.x);
    float diagonal_a = energy_line((cell.x * 0.5) + (cell.y * 0.8660254));
    float diagonal_b = energy_line((-cell.x * 0.5) + (cell.y * 0.8660254));
    float lattice = max(vertical, max(diagonal_a, diagonal_b));

    vec2 edge = abs(centered) * 2.0;
    float vignette = 1.0 - smoothstep(0.58, 1.0, max(edge.x, edge.y));
    float territory_mix = smoothstep(-0.34, 0.34, centered.y);
    vec3 accent = mix(far_color.rgb, grid_color.rgb, territory_mix);
    float accent_alpha = mix(far_color.a, grid_color.a, territory_mix);

    ALBEDO = base_color.rgb + (accent * lattice * vignette * 0.18);
    EMISSION = accent * lattice * vignette * 0.24;
    ALPHA = clamp(base_color.a + (accent_alpha * lattice * vignette), 0.0, 1.0);
}
