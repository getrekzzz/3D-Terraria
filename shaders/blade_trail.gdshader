// The streak a held blade leaves as it strikes (game/Player/BladeTrail.cs): a ribbon from part way up the blade to its
// tip, UV.y running from the inner edge to the tip edge. Vertex alpha carries the fade along the trail; the profile
// across it is drawn here: clear at the inner edge, brightest and whitest just inside the tip.
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 tint : source_color = vec4(1.0);
uniform float brightness = 1.0;

void fragment() {
	float across = UV.y;
	float profile = smoothstep(0.0, 0.8, across) * (1.0 - 0.55 * smoothstep(0.88, 1.0, across));
	float core = smoothstep(0.6, 0.95, across);
	ALBEDO = mix(tint.rgb, vec3(1.0), core * 0.55) * brightness;
	ALPHA = clamp(COLOR.a * profile, 0.0, 1.0);
}
