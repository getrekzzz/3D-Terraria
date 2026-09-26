// Leaf cards of tree crowns and bushes (`card:<layer>` slots): UV-mapped, alpha-tested sprigs from the props texture
// array, tinted by vertex colour (which also carries baked self-shadowing). The mesh's normals point away from the
// crown so the whole mass shades softly; both faces use them. Crowns sway with the wind and leaves flutter; light
// passes through the leaves.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"

global uniform float strata_time;
// xy wind direction (unit), z strength 0..1, w gust scroll (metres).
global uniform vec4 strata_wind;

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform float layer = 0.0;
// Crown sway (metres per metre of height above the origin).
uniform float sway = 0.012;
uniform float translucency = 0.75;
// MultiMesh instances carry voxel light in their custom data; single instances use the instance uniform.
uniform bool light_from_custom = false;
instance uniform vec4 voxel_light = vec4(0.0, 0.0, 0.0, 1.0);
instance uniform vec4 tint = vec4(1.0);

varying vec4 v_light;
varying vec3 v_world;
varying float f_sun_gate;

void vertex() {
	vec3 root = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float h = max(VERTEX.y - 1.0, 0.0);
	float wind = 0.6 + strata_wind.z * 1.2;
	float t = strata_time * 1.3 + root.x * 0.37 + root.z * 0.23;
	VERTEX.x += (sin(t) + strata_wind.x * 0.8) * sway * h * wind;
	VERTEX.z += (cos(t * 0.8) * 0.6 + strata_wind.y * 0.8) * sway * h * wind;
	// Leaves flutter: each card's tip (UV.y 0; the twig end is at 1) shivers on its own phase.
	float phase = dot(VERTEX, vec3(3.1, 1.7, 2.3));
	VERTEX += NORMAL * sin(strata_time * 7.0 + phase) * 0.02 * (1.0 - UV.y) * wind;
	v_light = light_from_custom ? INSTANCE_CUSTOM : voxel_light;
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 a = texture(albedo_array, vec3(UV, layer));
	// Mipmapping averages the leaves with the gaps; lift the far alpha so distant crowns stay full.
	vec2 texel = UV * vec2(textureSize(albedo_array, 0).xy);
	float lod = 0.5 * log2(max(dot(dFdx(texel), dFdx(texel)), dot(dFdy(texel), dFdy(texel))));
	ALPHA = clamp(a.a * (1.0 + max(lod, 0.0) * 0.3), 0.0, 1.0);
	ALPHA_SCISSOR_THRESHOLD = 0.5;
	// Both faces shade with the crown's outward normal.
	NORMAL = FRONT_FACING ? NORMAL : -NORMAL;
	vec3 albedo = a.rgb * COLOR.rgb * tint.rgb;
	ALBEDO = albedo;
	ROUGHNESS = 0.62;
	SPECULAR = 0.35;
	EMISSION = strata_block_light(albedo, v_light, 1.0);
	AO = strata_sky_visibility(v_light) * mix(0.55, 1.0, dot(COLOR.rgb, vec3(0.333)));
	AO_LIGHT_AFFECT = 0.25;
	f_sun_gate = strata_sun_gate(v_light) * strata_cloud_shadow(v_world, strata_light_dir);
}

void light() {
	strata_foliage_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, translucency, DIFFUSE_LIGHT);
	vec3 unused = vec3(0.0);
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION * 0.4, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, 0.0, ALBEDO, unused, SPECULAR_LIGHT);
}
