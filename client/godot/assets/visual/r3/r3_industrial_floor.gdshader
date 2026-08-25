shader_type spatial;
render_mode cull_back, depth_draw_opaque;

// A static, neutral industrial floor. The pattern intentionally continues well
// beyond the gameplay footprint and contains no board perimeter or player tint.
uniform vec4 base_color : source_color = vec4(0.105, 0.118, 0.125, 1.0);
uniform vec4 panel_color : source_color = vec4(0.145, 0.155, 0.158, 1.0);
uniform vec4 groove_color : source_color = vec4(0.025, 0.030, 0.033, 1.0);
uniform sampler2D floor_albedo : source_color, repeat_disable, filter_linear_mipmap_anisotropic;
uniform vec2 panel_count = vec2(31.30435, 22.941176);
uniform vec2 surface_size = vec2(80.0, 60.0);
uniform vec2 authored_size = vec2(46.0, 34.0);
uniform float panel_line_width : hint_range(0.005, 0.12) = 0.035;
uniform float texture_strength : hint_range(0.0, 1.0) = 0.82;
uniform float texture_edge_fade : hint_range(0.01, 0.3) = 0.11;
uniform float metallic : hint_range(0.0, 1.0) = 0.58;
uniform float roughness : hint_range(0.0, 1.0) = 0.54;

float hash21(vec2 value) {
    value = fract(value * vec2(123.34, 456.21));
    value += dot(value, value + 45.32);
    return fract(value.x * value.y);
}

float edge_line(float coordinate, float width) {
    float edge_distance = 0.5 - abs(fract(coordinate) - 0.5);
    return 1.0 - smoothstep(0.0, width, edge_distance);
}

void fragment() {
    vec2 panel_uv = UV * panel_count;
    vec2 panel_id = floor(panel_uv);
    float panel_edge = max(
        edge_line(panel_uv.x, panel_line_width),
        edge_line(panel_uv.y, panel_line_width));

    float panel_variation = hash21(panel_id) - 0.5;
    float brushed = sin((UV.x * 610.0) + (UV.y * 23.0)) * 0.5 + 0.5;
    float sparse_scuff = smoothstep(0.86, 0.99, hash21(floor(UV * vec2(92.0, 68.0))));

    vec3 surface_color = mix(base_color.rgb, panel_color.rgb, 0.36 + panel_variation * 0.16);
    surface_color += (brushed - 0.5) * 0.012;
    surface_color = mix(surface_color, groove_color.rgb, panel_edge * 0.74);
    surface_color = mix(surface_color, panel_color.rgb, sparse_scuff * 0.055);

    // Keep the authored image at its original 46x34 world-space density in the
    // center of the enlarged 80x60 open surface. Outside that footprint the
    // procedural steel continues without tiling or exposing a finite floor edge.
    vec2 authored_uv = (UV - vec2(0.5)) * (surface_size / authored_size) + vec2(0.5);
    vec3 authored_albedo = texture(floor_albedo, clamp(authored_uv, vec2(0.0), vec2(1.0))).rgb;
    float uv_edge_distance = min(
        min(authored_uv.x, 1.0 - authored_uv.x),
        min(authored_uv.y, 1.0 - authored_uv.y));
    float authored_weight = texture_strength * smoothstep(
        0.0,
        texture_edge_fade,
        uv_edge_distance);
    surface_color = mix(surface_color, authored_albedo, authored_weight);
    surface_color = mix(surface_color, groove_color.rgb, panel_edge * 0.18);

    ALBEDO = surface_color;
    METALLIC = metallic - panel_edge * 0.16;
    ROUGHNESS = clamp(roughness + panel_variation * 0.055 + panel_edge * 0.18, 0.0, 1.0);
}
