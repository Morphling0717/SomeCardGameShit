shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear_mipmap;
uniform vec4 tint_top : source_color = vec4(0.110, 0.240, 0.280, 0.64);
uniform vec4 tint_bottom : source_color = vec4(0.045, 0.120, 0.180, 0.54);
uniform vec4 edge_color : source_color = vec4(0.42, 0.96, 0.92, 0.64);
uniform float blur_lod : hint_range(0.0, 5.0, 0.1) = 2.4;
uniform float corner_radius_px : hint_range(8.0, 40.0, 1.0) = 18.0;
uniform float edge_width_px : hint_range(0.5, 4.0, 0.1) = 1.2;
uniform float highlight_strength : hint_range(0.0, 1.0, 0.01) = 0.34;

float rounded_box(vec2 uv, float radius_px, float inset_px) {
	vec2 uv_step = max(vec2(abs(dFdx(uv.x)), abs(dFdy(uv.y))), vec2(0.00001));
	vec2 panel_size = vec2(1.0) / uv_step;
	vec2 point = abs((uv - vec2(0.5)) * panel_size);
	vec2 half_extent = max(
		panel_size * 0.5 - vec2(inset_px + radius_px),
		vec2(0.0));
	vec2 delta = point - half_extent;
	float distance_to_edge = length(max(delta, vec2(0.0))) +
		min(max(delta.x, delta.y), 0.0) - radius_px;
	return 1.0 - smoothstep(-1.0, 1.0, distance_to_edge);
}

void fragment() {
	float outer = rounded_box(UV, corner_radius_px, 0.0);
	float inner = rounded_box(
		UV,
		max(corner_radius_px - edge_width_px, 1.0),
		edge_width_px);
	float border = clamp(outer - inner, 0.0, 1.0);
	vec4 blurred = textureLod(screen_texture, SCREEN_UV, blur_lod);
	vec4 tint = mix(tint_top, tint_bottom, smoothstep(0.0, 1.0, UV.y));
	float top_glint = pow(clamp(1.0 - UV.y, 0.0, 1.0), 8.0) * highlight_strength;
	vec3 glass_rgb = mix(blurred.rgb, tint.rgb, tint.a) + edge_color.rgb * top_glint;
	glass_rgb = mix(glass_rgb, edge_color.rgb, border * edge_color.a);
	float alpha = outer * max(tint.a, border * edge_color.a);
	COLOR = vec4(glass_rgb, alpha) * COLOR;
}
