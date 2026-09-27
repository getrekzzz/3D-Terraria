shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"
#include "res://shaders/include/terrain_common.gdshaderinc"

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_ids = CUSTOM0;
	v_light = CUSTOM1;
}

void fragment() {
	vec3 n = normalize(v_world_normal);
	bool crisp = (int(v_ids.w + 0.5) & 1) != 0;
	float tile = crisp ? crisp_tile_meters : natural_tile_meters;
	vec3 w = strata_tri_weights(n, crisp);
	vec4 albedo;
	vec4 mat;
	vec3 bent;
	if (abs(v_ids.x - v_ids.y) < 0.5 && abs(v_ids.x - v_ids.z) < 0.5) {
		strata_surface(v_ids.x, v_world_pos, n, w, tile, albedo, mat, bent);
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
	}
	float ao = COLOR.a;
	vec3 world_n = normalize(n + bent * 0.85 * normal_strength);
	vec3 base = strata_paint(strata_macro(albedo.rgb, v_world_pos), v_ids.w);
	ALBEDO = base;
	ROUGHNESS = mat.z;
	METALLIC = mat.w;
	NORMAL = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
	EMISSION = strata_block_light(base, v_light, ao);
	AO = ao * strata_sky_visibility(v_light);
	f_sun_gate = strata_sun_gate(v_light) * ao * strata_cloud_shadow(v_world_pos, strata_light_dir);
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, METALLIC, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
