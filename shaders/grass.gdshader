// Instanced grass clumps (game/World/GrassRenderer.cs). Each instance is one clump of tapered, curved blades
// (UV.x across a blade, UV.y from root 0 to tip 1, UV2.x a per-blade random number); flowers use the same shader
// with `flower` set (UV2.y marks petals).
//   COLOR (per instance)     rgb blade colour (linear), a tip glow
//   INSTANCE_CUSTOM          voxel light at the root (rgb block light, a sky exposure)
// Blades bend with the wind (gusts travel across the land), part around the player, shrink into the ground at
// the edge of the grass distance, and widen with distance so thinned-out far grass still covers the ground.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, world_vertex_coords;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"

global uniform float strata_time;
// xy wind direction (unit), z strength 0..1, w gust scroll (metres).
global uniform vec4 strata_wind;
// xyz the player's feet, w parting radius (metres).
global uniform vec4 strata_trample;

uniform float fade_start = 20.0;
uniform float fade_end = 30.0;
uniform bool flower = false;

varying vec4 v_light;
varying vec4 v_tint;
varying vec3 v_world;
varying float v_rand;
varying float f_sun_gate;

void vertex() {
	float t = UV.y;
	vec3 root = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	vec3 up = normalize(MODEL_MATRIX[1].xyz);
	float height = length(MODEL_MATRIX[1].xyz);
	float dist = length(root - CAMERA_POSITION_WORLD);
	float fade = 1.0 - smoothstep(fade_start, fade_end, dist);
	// Far clumps are thinned out by the renderer; widen what remains so the cover stays even.
	float widen = mix(1.0, 1.6, smoothstep(fade_start * 0.35, fade_end, dist));
	vec3 rel = VERTEX - root;
	float along = dot(rel, up);
	vec3 side = rel - up * along;
	rel = side * widen + up * along * fade;

	// Wind: a steady lean plus gusts rolling across the land and a quick per-clump flutter.
	vec2 dir = strata_wind.xy;
	float gust = texture(strata_cloud_noise, (root.xz - dir * strata_wind.w) / 48.0).r;
	float flutter = sin(strata_time * 3.1 + root.x * 1.7 + root.z * 1.3 + VERTEX.x * 4.0) * 0.5 + 0.5;
	float strength = strata_wind.z * (0.35 + 1.1 * gust) + 0.05;
	vec3 bend = vec3(dir.x, 0.0, dir.y) * strength * (0.75 + 0.25 * flutter) * 0.55;
	// The player parts the grass.
	vec3 away = root - strata_trample.xyz;
	float near = (1.0 - smoothstep(strata_trample.w * 0.4, strata_trample.w, length(away.xz))) * step(abs(away.y), 1.2);
	bend += vec3(normalize(away.xz + vec2(1e-4)).x, 0.0, normalize(away.xz + vec2(1e-4)).y) * near * 0.9;
	float k = t * t * height;
	vec3 offset = bend * k;
	rel += offset;
	// Keep the blade length: bending pulls the tip down.
	rel -= up * (length(offset) * length(offset) / max(height, 0.05)) * 0.5;
	VERTEX = root + rel;
	NORMAL = normalize(mix(NORMAL, up, 0.35) + vec3(offset.x, 0.0, offset.z) * 0.5);

	v_light = INSTANCE_CUSTOM;
	v_tint = COLOR;
	v_world = VERTEX;
	v_rand = fract(sin(float(INSTANCE_ID) * 12.9898) * 43758.5453);
}

void fragment() {
	float t = UV.y;
	vec3 tint = v_tint.rgb;
	vec3 base;
	if (flower) {
		// UV2.y marks petals (UV2.x runs from the flower's centre to the petal tip); the rest is the green stem.
		if (UV2.y > 0.5) {
			base = UV2.x < 0.2 ? vec3(0.8, 0.58, 0.1) : mix(tint, tint * 0.55 + 0.4, smoothstep(0.55, 1.0, UV2.x));
		} else {
			base = vec3(0.09, 0.18, 0.05);
		}
	} else {
		// Darker, cooler roots; lighter, warmer tips; a pale midrib and dry, bleached tips here and there.
		vec3 root_c = tint * vec3(0.32, 0.38, 0.3);
		vec3 tip_c = tint * 1.18 + vec3(0.05, 0.04, 0.0);
		base = mix(root_c, tip_c, pow(t, 0.7));
		float rib = 1.0 - abs(UV.x - 0.5) * 2.0;
		base *= 0.88 + 0.2 * rib;
		// UV2.x is a per-blade random number from the clump mesh.
		float dry = smoothstep(0.7, 1.0, t) * step(0.8, fract(v_rand + UV2.x * 7.31));
		base = mix(base, vec3(0.55, 0.5, 0.3) * (0.5 + dot(tint, vec3(0.6))), dry * 0.45);
	}
	float sky = strata_sky_visibility(v_light);
	float ao = mix(0.3, 1.0, smoothstep(0.0, 0.6, t));
	ALBEDO = base;
	ROUGHNESS = 0.55;
	SPECULAR = 0.35;
	AO = ao * sky;
	AO_LIGHT_AFFECT = 0.5;
	EMISSION = strata_block_light(base, v_light, ao) + base * v_tint.a * smoothstep(0.4, 1.0, t) * 2.5;
	f_sun_gate = strata_sun_gate(v_light) * strata_cloud_shadow(v_world, strata_light_dir);
}

void light() {
	strata_foliage_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, 0.7, DIFFUSE_LIGHT);
	vec3 unused = vec3(0.0);
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION * 0.5, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, 0.0, ALBEDO, unused, SPECULAR_LIGHT);
}
