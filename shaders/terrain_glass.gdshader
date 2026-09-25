shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/terrain_common.gdshaderinc"

// Glass, ice, crystal: translucent with a fresnel rim; glowing crystals add their own light via EMISSION.
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
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = albedo.rgb;
	ALPHA = clamp(albedo.a + fresnel * 0.4, 0.0, 1.0);
	ROUGHNESS = 0.05;
	EMISSION = strata_ambient(albedo.rgb, v_light, 1.0) + albedo.rgb * fresnel * 0.15;
	f_sun_gate = strata_sun_gate(v_light);
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, 0.05, 0.0, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
