// Shared body of the prop shaders (prop.gdshader, prop_clear.gdshader): object-space triplanar sampling of the props
// texture array (1 m tiles, matching the Blender box projection), tinted by vertex colour, lit by baked voxel light.
// Define PROP_CLEAR before including for the translucent variant.
#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"

global uniform float strata_time;

uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray material_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float layer = 0.0;
uniform float glow = 0.0;
// Tinted metal slots (prop:<layer>:metal): the layer is read as neutral grey normalised to this gain (linear), so the
// vertex colour is the metal's colour instead of multiplying onto the layer's own (muddy) grey. 0 = plain multiply.
uniform float neutral_gain = 0.0;
// Foliage sway (metres per metre of height above the origin).
uniform float sway = 0.0;
// Thin foliage (leaf layers): wrapped, light-transmitting diffuse instead of the opaque BRDF.
uniform float foliage = 0.0;
// MultiMesh instances carry voxel light in their custom data; single instances use the instance uniform.
uniform bool light_from_custom = false;
#ifdef PROP_CLEAR
// Opacity of translucent parts seen face on (prop:<layer>:clear slots).
uniform float clarity = 0.5;
#endif
instance uniform vec4 voxel_light = vec4(0.0, 0.0, 0.0, 1.0);
// Per-instance colour multiplier (enemy variants, hurt flash via values above 1).
instance uniform vec4 tint = vec4(1.0);

varying vec3 v_obj_pos;
varying vec3 v_obj_normal;
varying vec4 v_light;
varying vec3 v_world;
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
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
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
	vec3 texel = a.rgb;
	if (neutral_gain > 0.0) {
		texel = vec3(min(dot(texel, vec3(0.2126, 0.7152, 0.0722)) * neutral_gain, 1.0));
	}
	vec3 albedo = texel * COLOR.rgb * tint.rgb;
	ALBEDO = albedo;
	ROUGHNESS = m.z;
	METALLIC = m.w;
#ifdef PROP_CLEAR
	// Translucent gel: see-through face on, thicker toward the rim; vertex alpha scales it per part.
	float facing = abs(dot(normalize(NORMAL), normalize(VIEW)));
	ALPHA = clamp(COLOR.a * mix(clarity + 0.3, clarity, facing), 0.0, 1.0);
	ROUGHNESS = min(ROUGHNESS, 0.18);
#endif
	EMISSION = strata_block_light(albedo, v_light, 1.0) + COLOR.rgb * glow;
	AO = strata_sky_visibility(v_light);
	f_sun_gate = strata_sun_gate(v_light) * strata_cloud_shadow(v_world, strata_light_dir);
}

void light() {
	if (foliage > 0.0) {
		strata_foliage_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, foliage, DIFFUSE_LIGHT);
	} else {
		strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, METALLIC, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
	}
}
