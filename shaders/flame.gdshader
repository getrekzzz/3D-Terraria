shader_type spatial;
// A living flame (torches, candles, the held torch): an upright camera-facing quad whose shape and heat are
// procedural turbulence rising through a teardrop — a near-white core at the base, a yellow-orange body and a
// flickering deep-red tip that breaks up into licks. Additive HDR, so the glow pass blooms it.
// MultiMesh custom data: x = phase (0..1, desynchronises neighbours), y = height in metres.
render_mode blend_add, unshaded, cull_disabled, depth_draw_never, shadows_disabled, skip_vertex_transform;

#include "res://shaders/include/noise.gdshaderinc"

uniform vec4 flame_color : source_color = vec4(1.0, 0.54, 0.16, 1.0);
uniform float intensity = 1.7;
global uniform vec4 strata_wind;

varying vec2 v_uv;
varying float v_phase;
varying float v_fade;

void vertex() {
	float height = INSTANCE_CUSTOM.y > 0.0 ? INSTANCE_CUSTOM.y : 0.2;
	v_phase = INSTANCE_CUSTOM.x;
	v_uv = VERTEX.xy + 0.5; // the quad spans -0.5..0.5
	vec3 base = MODEL_MATRIX[3].xyz;
	vec3 cam = INV_VIEW_MATRIX[3].xyz;
	vec3 to_cam = cam - base;
	float dist = length(to_cam);
	to_cam.y = 0.0;
	to_cam = length(to_cam) > 1e-4 ? normalize(to_cam) : vec3(0.0, 0.0, 1.0);
	vec3 right = normalize(cross(vec3(0.0, 1.0, 0.0), to_cam));
	// Wider than tall-flame silhouettes need so the licks have room; the top leans a little with the wind.
	float t = TIME + v_phase * 13.0;
	vec3 lean = vec3(strata_wind.x, 0.0, strata_wind.y) * strata_wind.z * 0.25 + right * sin(t * 2.1) * 0.06;
	vec3 world = base + right * VERTEX.x * height * 0.7 + vec3(0.0, 1.0, 0.0) * v_uv.y * height * 1.15
		+ lean * v_uv.y * v_uv.y * height + to_cam * 0.01;
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	NORMAL = normalize((VIEW_MATRIX * vec4(to_cam, 0.0)).xyz);
	// Tiny at a distance: fade out rather than shimmer as a sub-pixel speck.
	v_fade = 1.0 - smoothstep(40.0, 60.0, dist);
}

void fragment() {
	float t = TIME * 1.15 + v_phase * 17.0;
	vec2 uv = v_uv;
	// The whole flame breathes: its height flickers with slow noise.
	float flick = 0.82 + 0.18 * strata_value_noise(vec2(t * 2.4, v_phase * 9.0));
	float y = uv.y / flick;
	// Rising turbulence bends the flame sideways, more towards the tip.
	float turb = strata_fbm3(vec2(uv.x * 2.6 + v_phase * 5.0, uv.y * 2.2 - t * 2.3));
	float fine = strata_value_noise(vec2(uv.x * 7.0, uv.y * 6.0 - t * 4.1));
	float x = (uv.x - 0.5) * 2.0 + (turb - 0.5) * 0.9 * y * y;
	// Teardrop: a round base, widest low down, pinched to licks at the top.
	float width = 0.62 * sqrt(clamp(y * 3.2, 0.0, 1.0)) * (1.0 - smoothstep(0.25, 1.0, y));
	width *= 0.85 + 0.3 * fine;
	float edge = abs(x) / max(width, 1e-3);
	float body = 1.0 - smoothstep(0.55, 1.0, edge);
	body *= 1.0 - smoothstep(0.72, 1.0, y + (fine - 0.5) * 0.35);
	// Detached licks above the tip.
	float lick = smoothstep(0.6, 0.85, strata_fbm3(vec2(uv.x * 4.0, uv.y * 3.0 - t * 3.0))) * smoothstep(0.55, 0.8, y) * (1.0 - smoothstep(0.9, 1.05, y))
		* (1.0 - smoothstep(0.1, 0.35, abs(uv.x - 0.5)));
	float mask = clamp(max(body, lick * 0.7), 0.0, 1.0);
	// Temperature: a small white-hot core low in the middle, an orange body, cooling to red outwards and upwards.
	float core = (1.0 - smoothstep(0.0, 0.55, edge)) * (1.0 - smoothstep(0.08, 0.45, y));
	float heat = clamp(0.62 * (1.0 - edge * 0.8) * (1.0 - y * 0.9) + core * 0.45 + (fine - 0.5) * 0.18, 0.0, 1.0) * mask;
	vec3 deep = vec3(0.55, 0.06, 0.01);
	vec3 body_col = flame_color.rgb;
	vec3 yellow = vec3(1.0, 0.82, 0.42);
	vec3 white = vec3(1.0, 0.96, 0.86);
	vec3 col = mix(deep, body_col, smoothstep(0.0, 0.35, heat));
	col = mix(col, yellow, smoothstep(0.35, 0.7, heat));
	col = mix(col, white, smoothstep(0.72, 0.95, heat));
	// A faint blue root where the fuel meets the air.
	float root = smoothstep(0.14, 0.02, y) * (1.0 - smoothstep(0.0, 0.7, edge));
	col = mix(col, vec3(0.3, 0.45, 1.0), root * 0.45);
	float brightness = intensity * (0.3 + 1.6 * heat * heat) * (0.9 + 0.2 * flick);
	ALBEDO = col * brightness;
	ALPHA = mask * v_fade * clamp(0.25 + heat * 1.2, 0.0, 1.0);
}
