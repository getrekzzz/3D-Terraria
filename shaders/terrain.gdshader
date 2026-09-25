shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
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
	vec4 albedo = vec4(0.0);
	vec4 mat = vec4(0.0);
	vec3 bent = vec3(0.0);
	vec3 bary = COLOR.rgb;
	if (abs(v_ids.x - v_ids.y) < 0.5 && abs(v_ids.x - v_ids.z) < 0.5) {
		strata_sample_surface(v_ids.x, v_world_pos, n, w, tile, 1.0, albedo, mat, bent);
	} else {
		float total = max(bary.x + bary.y + bary.z, 1e-4);
		strata_sample_surface(v_ids.x, v_world_pos, n, w, tile, bary.x / total, albedo, mat, bent);
		strata_sample_surface(v_ids.y, v_world_pos, n, w, tile, bary.y / total, albedo, mat, bent);
		strata_sample_surface(v_ids.z, v_world_pos, n, w, tile, bary.z / total, albedo, mat, bent);
	}
	float ao = COLOR.a;
	vec3 world_n = normalize(n + bent * 0.85);
	ALBEDO = albedo.rgb;
	ROUGHNESS = mat.z;
	METALLIC = mat.w;
	NORMAL = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
	EMISSION = strata_ambient(albedo.rgb, v_light, ao);
	AO = ao;
	f_sun_gate = strata_sun_gate(v_light) * ao;
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, METALLIC, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
