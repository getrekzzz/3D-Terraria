// Generated props (trees, furniture, items, creatures): object-space triplanar sampling of the props texture
// array (1 m tiles, matching the Blender box projection), tinted by vertex colour, lit by baked voxel light.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"

global uniform float strata_time;

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray material_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float layer = 0.0;
uniform float glow = 0.0;
// Foliage sway (metres per metre of height above the origin).
uniform float sway = 0.0;
// MultiMesh instances carry voxel light in their custom data; single instances use the instance uniform.
uniform bool light_from_custom = false;
instance uniform vec4 voxel_light = vec4(0.0, 0.0, 0.0, 1.0);

varying vec3 v_obj_pos;
varying vec3 v_obj_normal;
varying vec4 v_light;
varying float f_sun_gate;

void vertex() {
	v_obj_pos = VERTEX;
	v_obj_normal = NORMAL;
	if (sway > 0.0) {
		vec3 world = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
		float h = max(VERTEX.y - 1.0, 0.0);
		float t = strata_time * 1.3 + world.x * 0.37 + world.z * 0.23;
		VERTEX.x += sin(t) * sway * h;
		VERTEX.z += cos(t * 0.8) * sway * 0.6 * h;
	}
	v_light = light_from_custom ? INSTANCE_CUSTOM : voxel_light;
}

void fragment() {
	vec3 n = normalize(v_obj_normal);
	vec3 w = pow(abs(n), vec3(4.0));
	w /= (w.x + w.y + w.z);
	vec3 p = v_obj_pos;
	vec4 a = w.x * texture(albedo_array, vec3(p.zy * vec2(1.0, -1.0), layer))
		+ w.y * texture(albedo_array, vec3(p.xz, layer))
		+ w.z * texture(albedo_array, vec3(p.xy * vec2(1.0, -1.0), layer));
	vec4 m = w.x * texture(material_array, vec3(p.zy * vec2(1.0, -1.0), layer))
		+ w.y * texture(material_array, vec3(p.xz, layer))
		+ w.z * texture(material_array, vec3(p.xy * vec2(1.0, -1.0), layer));
	vec3 albedo = a.rgb * COLOR.rgb;
	ALBEDO = albedo;
	ROUGHNESS = m.z;
	METALLIC = m.w;
	EMISSION = strata_ambient(albedo, v_light, 1.0) + COLOR.rgb * glow;
	f_sun_gate = strata_sun_gate(v_light);
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, METALLIC, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
