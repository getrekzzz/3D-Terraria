shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"
#include "res://shaders/include/terrain_common.gdshaderinc"
#include "res://shaders/include/weather.gdshaderinc"

global uniform float strata_time;
global uniform vec3 strata_camera;

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_ids = CUSTOM0;
	v_light = CUSTOM1;
}

// How far ore glints reach (graphics preset oreGlintDistance); they fade over the last 30 %.
uniform float ore_glint_distance = 24.0;

// A surface's ore look: x = how strongly it glints (layer table .z, 0 for everything that is not ore), y = soft
// self-glow (surface table .w, BlockDef.glow), z = the ore tint packed as 0xRRGGBB (layer table .w).
vec3 ore_info(float id) {
	vec4 layers = texelFetch(surface_table, ivec2(int(id + 0.5), 0), 0);
	vec4 t = texelFetch(layer_table, ivec2(int(layers.y + 0.5), 0), 0);
	return vec3(t.z, layers.w, t.w);
}

vec3 unpack_tint(float packed) {
	int c = int(packed); // exact: 0xRRGGBB < 2^24 is an integer-valued float
	return pow(vec3(float((c >> 16) & 255), float((c >> 8) & 255), float(c & 255)) / 255.0, vec3(2.2));
}

// One octave of glints: sparse points on a world grid of `freq` cells per metre that twinkle as the eye moves.
float glint_octave(float freq, float density, vec3 view, float seed) {
	vec3 g = floor(v_world_pos * freq);
	float h = fract(sin(dot(g, vec3(12.9898, 78.233, 37.719)) + seed) * 43758.5453);
	if (h < 1.0 - density) return 0.0;
	float dot_shape = 1.0 - smoothstep(0.1, 0.36, length(fract(v_world_pos * freq) - 0.5));
	return dot_shape * pow(max(0.0, sin(strata_time * (1.2 + h * 2.5) + h * 40.0 + dot(view, vec3(3.1, 5.7, 2.3)) * 7.0)), 8.0);
}

// Metallic grains catching the light: a fine octave up close and a coarse one that keeps veins twinkling out to the
// preset's glint distance. Only ore flecks glint (`fleck`: the material's metallic channel). Bounded cost: two hashes,
// no texture fetches, nothing past the distance.
vec3 ore_glints(vec3 tint, float sparkle, float fleck, vec3 view, vec4 light) {
	if (sparkle <= 0.0) return vec3(0.0);
	float dist = length(v_world_pos - strata_camera); // built-ins like INV_VIEW_MATRIX exist only inside fragment()
	float near = 1.0 - smoothstep(ore_glint_distance * 0.7, ore_glint_distance, dist);
	if (near <= 0.0) return vec3(0.0);
	// Far away the mipmapped mask averages the flecks out; the coarse glints stand for whole veins there.
	float mask = mix(fleck, 1.0, smoothstep(6.0, 16.0, dist));
	float fine = (1.0 - smoothstep(6.0, 12.0, dist)) * glint_octave(18.0, 0.16, view, 0.0);
	float coarse = smoothstep(4.0, 10.0, dist) * glint_octave(5.0, 0.22, view, 17.0);
	// Glints need light to catch (torchlight, daylight), with a floor so veins can be spotted in the dark.
	float lit = max(max(light.r, max(light.g, light.b)), light.a * strata_daylight);
	return mix(tint, vec3(1.0), 0.35) * (fine + coarse * 0.8) * sparkle * near * mask * (0.5 + 3.0 * lit);
}

void fragment() {
	vec3 n = normalize(v_world_normal);
	vec3 ore = vec3(0.0);
	vec3 tint = vec3(0.0);
	bool crisp = (int(v_ids.w + 0.5) & 1) != 0;
	float tile = crisp ? crisp_tile_meters : natural_tile_meters;
	vec3 w = strata_tri_weights(n, crisp);
	vec4 albedo;
	vec4 mat;
	vec3 bent;
	if (abs(v_ids.x - v_ids.y) < 0.5 && abs(v_ids.x - v_ids.z) < 0.5) {
		strata_surface(v_ids.x, v_world_pos, n, w, tile, albedo, mat, bent);
		ore = ore_info(v_ids.x);
		if (ore.x > 0.0) tint = unpack_tint(ore.z);
	} else {
		// Three surfaces meet in this triangle: the one standing highest (texture height plus its barycentric
		// weight) wins, so pebbles and grass tufts poke through instead of cross-fading.
		vec3 bary = COLOR.rgb / max(COLOR.r + COLOR.g + COLOR.b, 1e-4);
		vec4 a0 = vec4(0.0), a1 = vec4(0.0), a2 = vec4(0.0);
		vec4 m0 = vec4(0.0), m1 = vec4(0.0), m2 = vec4(0.0);
		vec3 b0 = vec3(0.0), b1 = vec3(0.0), b2 = vec3(0.0);
		if (bary.x > 0.002) strata_surface(v_ids.x, v_world_pos, n, w, tile, a0, m0, b0);
		if (bary.y > 0.002) strata_surface(v_ids.y, v_world_pos, n, w, tile, a1, m1, b1);
		if (bary.z > 0.002) strata_surface(v_ids.z, v_world_pos, n, w, tile, a2, m2, b2);
		vec3 h = vec3(a0.a, a1.a, a2.a) * 0.6 + bary;
		float top = max(h.x, max(h.y, h.z)) - blend_depth;
		vec3 k = max(h - top, vec3(0.0)) * step(vec3(0.002), bary);
		k /= max(k.x + k.y + k.z, 1e-4);
		albedo = a0 * k.x + a1 * k.y + a2 * k.z;
		mat = m0 * k.x + m1 * k.y + m2 * k.z;
		bent = b0 * k.x + b1 * k.y + b2 * k.z;
		vec3 o0 = ore_info(v_ids.x), o1 = ore_info(v_ids.y), o2 = ore_info(v_ids.z);
		ore.xy = k.x * o0.xy + k.y * o1.xy + k.z * o2.xy;
		if (ore.x > 0.0) // weighted by how much each surface glints, so the stone beside a vein does not darken it
			tint = (k.x * o0.x * unpack_tint(o0.z) + k.y * o1.x * unpack_tint(o1.z) + k.z * o2.x * unpack_tint(o2.z)) / ore.x;
	}
	float ao = COLOR.a;
	vec3 world_n = normalize(n + bent * 0.85 * normal_strength);
	vec3 base = strata_paint(strata_macro(albedo.rgb, v_world_pos), v_ids.w);
	float roughness = mat.z;
	strata_wet(n, v_world_pos, v_light.a, true, base, roughness, world_n);
	ALBEDO = base;
	ROUGHNESS = roughness;
	METALLIC = mat.w;
	NORMAL = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
	// Ore flecks (metallic texels) shine and softly glow; the stone around them stays dark and lights nothing.
	// Metal ores mark their flecks metallic (~0.9); gems and hellstone nuggets only a little (0.1-0.2), so any metallic
	// texel counts fully.
	float fleck = max(clamp(mat.w * 2.5, 0.0, 1.0), smoothstep(0.03, 0.08, mat.w));
	EMISSION = strata_block_light(base, v_light, ao) + (ore_glints(tint, ore.x, fleck, VIEW, v_light) + base * ore.y * fleck) * ao;
	AO = ao * strata_sky_visibility(v_light);
	f_sun_gate = strata_sun_gate(v_light) * ao * strata_cloud_shadow(v_world_pos, strata_light_dir);
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, METALLIC, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
