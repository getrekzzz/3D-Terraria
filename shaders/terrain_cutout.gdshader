shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"
#include "res://shaders/include/terrain_common.gdshaderinc"

// Leaves, webs: alpha-tested, double sided.
void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_ids = CUSTOM0;
	v_light = CUSTOM1;
}

void fragment() {
	vec3 n = normalize(v_world_normal);
	vec3 w = strata_tri_weights(n, true);
	vec4 albedo = vec4(0.0);
	vec4 mat = vec4(0.0);
	vec3 bent = vec3(0.0);
	strata_sample_surface(v_ids.x, v_world_pos, n, w, crisp_tile_meters, 1.0, albedo, mat, bent);
	ALBEDO = albedo.rgb;
	ALPHA = albedo.a;
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	ROUGHNESS = mat.z;
	EMISSION = strata_block_light(albedo.rgb, v_light, 1.0);
	AO = strata_sky_visibility(v_light);
	f_sun_gate = strata_sun_gate(v_light) * strata_cloud_shadow(v_world_pos, strata_light_dir);
}

void light() {
	strata_foliage_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, 0.6, DIFFUSE_LIGHT);
}
