shader_type canvas_item;
render_mode unshaded;

uniform vec4 ring_color : source_color = vec4(0.52, 0.98, 0.92, 0.9);
uniform float ring_width : hint_range(0.005, 0.12, 0.005) = 0.035;

void fragment() {
	vec4 source = texture(TEXTURE, UV);
	float distance_from_center = length(UV - vec2(0.5));
	float mask = 1.0 - smoothstep(0.475, 0.5, distance_from_center);
	float inner = 1.0 - smoothstep(0.475 - ring_width, 0.475, distance_from_center);
	float ring = clamp(mask - inner, 0.0, 1.0);
	vec3 color = mix(source.rgb, ring_color.rgb, ring * ring_color.a);
	COLOR = vec4(color, source.a * mask) * COLOR;
}
