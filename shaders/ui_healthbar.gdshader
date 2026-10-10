// Enemy mini health bar: a camera-facing bar over a hurt creature, a dark frame around a fill that slides from green
// through yellow to red as life drops. The node's scale is the bar's size in metres (the shared mesh is a unit quad).
// Beyond READABLE metres the bar grows with distance, so it keeps its on-screen size and stays legible.
shader_type spatial;
render_mode unshaded, blend_mix, cull_disabled, depth_draw_never, shadows_disabled;

instance uniform float fraction = 1.0;

uniform vec3 full_color : source_color = vec3(0.24, 0.86, 0.24);
uniform vec3 half_color : source_color = vec3(0.98, 0.84, 0.18);
uniform vec3 empty_color : source_color = vec3(0.88, 0.14, 0.11);
uniform vec4 frame_color : source_color = vec4(0.05, 0.05, 0.07, 0.95);
uniform vec4 back_color : source_color = vec4(0.10, 0.09, 0.12, 0.6);

const float READABLE = 9.0;

varying vec2 v_size;

void vertex() {
	vec2 size = vec2(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz));
	vec3 origin = MODEL_MATRIX[3].xyz;
	float grow = max(1.0, length(INV_VIEW_MATRIX[3].xyz - origin) / READABLE);
	// Grow upward from the bar's bottom edge so a far bar never sinks into the creature's head.
	origin.y += size.y * (grow - 1.0) * 0.5;
	size *= grow;
	v_size = size;
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
		vec4(INV_VIEW_MATRIX[0].xyz * size.x, 0.0),
		vec4(INV_VIEW_MATRIX[1].xyz * size.y, 0.0),
		vec4(INV_VIEW_MATRIX[2].xyz, 0.0),
		vec4(origin, 1.0));
}

void fragment() {
	// Frame: the same thickness on both axes (UV scaled by the bar's aspect), never thinner than one screen pixel.
	vec2 frame = max(vec2(v_size.y * 0.12) / v_size, fwidth(UV));
	bool on_frame = UV.x < frame.x || UV.x > 1.0 - frame.x || UV.y < frame.y || UV.y > 1.0 - frame.y;
	float f = clamp(fraction, 0.0, 1.0);
	float x = (UV.x - frame.x) / max(1e-4, 1.0 - 2.0 * frame.x);
	vec3 fill = f > 0.5 ? mix(half_color, full_color, (f - 0.5) * 2.0) : mix(empty_color, half_color, f * 2.0);
	// A darker lower half reads as the bevel of Terraria's bar.
	float y = (UV.y - frame.y) / max(1e-4, 1.0 - 2.0 * frame.y);
	fill *= y < 0.45 ? 1.0 : 0.8;
	vec4 c = x <= f ? vec4(fill, 1.0) : back_color;
	if (on_frame) c = frame_color;
	ALBEDO = c.rgb;
	ALPHA = c.a;
}
