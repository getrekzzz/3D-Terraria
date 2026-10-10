// Clear parts of generated people (char:glass slots): a Martian's helmet dome, a diver's visor. Thin tinted glass,
// nearly clear face on and more reflective toward its rim, with the sun's sharp highlight; drawn after opaque
// geometry so the head inside shows through. Vertex colour = the glass's tint (its alpha, the baked occlusion, is
// not used: light passes the glass).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"

// Opacity of the glass seen face on, and how much more it reflects at grazing angles.
uniform float clarity = 0.1;
uniform float rim = 0.55;
// The same instance uniforms, in the same order, as character.gdshader (one character mesh carries both shaders).
instance uniform vec4 voxel_light = vec4(0.0, 0.0, 0.0, 1.0);
instance uniform vec4 tint = vec4(1.0);
instance uniform float recolor = 0.0;
instance uniform vec4 hair_color = vec4(0.216, 0.216, 0.216, 1.0);
instance uniform vec4 skin_color = vec4(0.745, 0.434, 0.279, 1.0);
instance uniform vec4 shirt_color = vec4(0.434, 0.080, 0.080, 1.0);
instance uniform vec4 pants_color = vec4(0.080, 0.352, 0.080, 1.0);
instance uniform vec4 shoe_color = vec4(0.216, 0.216, 0.216, 1.0);

varying vec4 v_light;
varying vec3 v_world;
varying float f_sun_gate;

void vertex() {
	v_light = voxel_light;
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec3 base = COLOR.rgb * tint.rgb;
	float facing = clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0);
	float fresnel = pow(1.0 - facing, 3.0);
	ALBEDO = base;
	ALPHA = clamp(clarity + fresnel * rim, 0.0, 0.9);
	ROUGHNESS = 0.04;
	METALLIC = 0.0;
	SPECULAR = 0.6;
	EMISSION = strata_block_light(base, v_light, 1.0) * 0.5 + base * fresnel * 0.15 * strata_sky_visibility(v_light);
	AO = strata_sky_visibility(v_light);
	f_sun_gate = strata_sun_gate(v_light) * strata_cloud_shadow(v_world, strata_light_dir);
}

void light() {
	vec3 diffuse = vec3(0.0);
	vec3 spec = vec3(0.0);
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, 0.04, 0.0, ALBEDO,
		diffuse, spec);
	DIFFUSE_LIGHT += diffuse;
	SPECULAR_LIGHT += spec;
}
