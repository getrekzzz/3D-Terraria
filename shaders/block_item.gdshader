// A block as an item (P3-06): a small cube (game/Items/BlockItemMeshes.cs) textured from the terrain texture arrays,
// one layer per face (UV2.x; UV2.y the block's self-glow), lit like props by the voxel light at its holder
// (instance uniform). Held blocks in third and first person; dropped blocks later.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray material_array : filter_linear_mipmap_anisotropic, repeat_enable;
instance uniform vec4 voxel_light = vec4(0.0, 0.0, 0.0, 1.0);
instance uniform vec4 tint = vec4(1.0);

varying flat vec2 v_layer;
varying vec3 v_world;
varying float f_sun_gate;

void vertex() {
	v_layer = UV2;
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 a = texture(albedo_array, vec3(UV, v_layer.x));
	vec4 m = texture(material_array, vec3(UV, v_layer.x));
	vec3 albedo = a.rgb * tint.rgb;
	ALBEDO = albedo;
	ROUGHNESS = m.z;
	METALLIC = m.w;
	EMISSION = strata_block_light(albedo, voxel_light, 1.0) + albedo * v_layer.y;
	AO = strata_sky_visibility(voxel_light);
	f_sun_gate = strata_sun_gate(voxel_light) * strata_cloud_shadow(v_world, strata_light_dir);
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, METALLIC, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
