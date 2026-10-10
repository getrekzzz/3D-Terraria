// People (player, town folk, humanoid foes): one shader, selected per surface by `kind`.
//   0 skin   pores and fine folds from the detail array, subsurface scattering, wrapped (translucent) diffuse
//   1 eye    wet, glossy
//   2 hair   strands along the UVs (U runs along the hair), anisotropic highlights from the exported tangents
//   3 nail   smooth keratin
//   4 cloth  woven texture (props array), soft sheen at grazing angles
//   5 leather, 6 metal, 7 bone: props array texture with their own roughness/metalness
//   8 glow   self-lit (undead eyes)
// Surface detail is sampled in the character's rest pose, rebuilt from the two UV sets the generator writes
// (UV = rest x, z; UV2 = rest y, z), so it stays attached to skin and cloth while the rig animates.
// The vertex colour's alpha is ambient occlusion baked by the generator (1 open, 0 enclosed): it darkens sky and
// block light fully and direct light partly (eye sockets, under the jaw, between locks of hair, inside folds).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"

uniform int kind = 0;
uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray material_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray detail_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float layer = 0.0;
uniform float tile = 0.1;
uniform float detail_layer = 7.0;
uniform float detail_strength = 0.0;
uniform float detail_tile = 0.02;
uniform float roughness_base = 0.6;
uniform float metallic_base = 0.0;
uniform float sss = 0.0;
uniform float sheen = 0.0;
instance uniform vec4 voxel_light = vec4(0.0, 0.0, 0.0, 1.0);
instance uniform vec4 tint = vec4(1.0);
// Player characters are generated with marker colours (grey hair, belt and boots, a red shirt, a green pair of trousers,
// a blue undershirt and irises)
// and recoloured to the character's chosen look; linear colours, recolor = 1 enables it.
instance uniform float recolor = 0.0;
instance uniform vec4 hair_color = vec4(0.216, 0.216, 0.216, 1.0);
instance uniform vec4 skin_color = vec4(0.745, 0.434, 0.279, 1.0);
instance uniform vec4 shirt_color = vec4(0.434, 0.080, 0.080, 1.0);
instance uniform vec4 pants_color = vec4(0.080, 0.352, 0.080, 1.0);
instance uniform vec4 shoe_color = vec4(0.216, 0.216, 0.216, 1.0);
// The undershirt (vest and coat clothing styles) is generated blue, the iris of the eyes the same blue.
instance uniform vec4 undershirt_color = vec4(0.080, 0.080, 0.434, 1.0);
instance uniform vec4 eye_color = vec4(0.080, 0.080, 0.434, 1.0);
// Secondary motion (X-17, game/Rendering/CreatureVisual.Sway.cs): xyz = how far loose parts are pushed (model space,
// metres), w = 1 while the character is close enough to animate them. Hair below the head joint (sway_head, rest
// height) and cloth whose UVSway map (CUSTOM0.x, skirts.mark_sway: 0 held .. 0.9 free) marks it loose (skirts, robes,
// coat tails, sash ends) bend by it, plus wind.
instance uniform vec4 sway = vec4(0.0);
instance uniform float sway_head = 1.575;
global uniform vec4 strata_wind;
global uniform float strata_time;

varying vec3 v_rest;
varying vec3 v_rest_normal;
varying vec3 v_world;
varying vec4 v_light;
varying float f_sun_gate;
varying vec3 f_tangent;
varying float f_occ;
varying float f_shift;

void vertex() {
	// glTF flips V; Blender (x, y, z) becomes Godot (x, z, -y).
	v_rest = vec3(UV.x, 1.0 - UV.y, -UV2.x);
	v_rest_normal = NORMAL;
	if (sway.w > 0.0) {
		float h = 1.0 - UV2.y; // rest height (UVRest = rest y, z)
		float w = kind == 2 ? clamp((sway_head + 0.05 - h) / 0.35, 0.0, 1.0)
			: kind == 4 ? CUSTOM0.x : 0.0;
		if (w > 0.0) {
			vec3 wind = transpose(mat3(MODEL_MATRIX)) * vec3(strata_wind.x, 0.0, strata_wind.y) * strata_wind.z;
			float flutter = 0.5 + 0.5 * sin(strata_time * 3.1 + h * 9.0 + VERTEX.x * 6.0);
			VERTEX += (sway.xyz + wind * (0.025 + 0.035 * flutter)) * w;
		}
	}
	v_light = voxel_light;
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

vec3 tri_weights(vec3 n) {
	vec3 w = pow(abs(n), vec3(4.0));
	return w / (w.x + w.y + w.z);
}

// Triplanar sample of one texture array layer in rest space; returns rgba and adds a normal perturbation.
vec4 rest_sample(sampler2DArray tex, float lay, vec3 p, vec3 w, float scale) {
	return w.x * texture(tex, vec3(p.zy / scale, lay)) + w.y * texture(tex, vec3(p.xz / scale, lay)) + w.z * texture(tex, vec3(p.xy / scale, lay));
}

vec3 rest_bump(sampler2DArray tex, float lay, vec3 p, vec3 n, vec3 w, float scale) {
	vec2 tx = texture(tex, vec3(p.zy / scale, lay)).xy * 2.0 - 1.0;
	vec2 ty = texture(tex, vec3(p.xz / scale, lay)).xy * 2.0 - 1.0;
	vec2 tz = texture(tex, vec3(p.xy / scale, lay)).xy * 2.0 - 1.0;
	return w.x * vec3(0.0, tx.y, tx.x) + w.y * vec3(ty.x, 0.0, ty.y) + w.z * vec3(tz.x, tz.y, 0.0);
}

// The chosen colour carrying the marker's shading (folds, streaks, roots) as a brightness ratio.
vec3 recolored(vec3 painted, vec3 marker, vec3 chosen) {
	const vec3 lw = vec3(0.2126, 0.7152, 0.0722);
	return chosen * dot(painted, lw) / max(dot(marker, lw), 1e-3);
}

void fragment() {
	vec3 base = COLOR.rgb * tint.rgb;
	if (recolor > 0.5) {
		if (kind == 2) base = recolored(COLOR.rgb, vec3(0.216), hair_color.rgb) * tint.rgb;
		else if (kind == 0) base = COLOR.rgb * skin_color.rgb / vec3(0.745, 0.434, 0.279) * tint.rgb;
		else if (kind == 4) base = (COLOR.b > max(COLOR.r, COLOR.g)
			? recolored(COLOR.rgb, vec3(0.080, 0.080, 0.434), undershirt_color.rgb)
			: COLOR.r > COLOR.g
			? recolored(COLOR.rgb, vec3(0.434, 0.080, 0.080), shirt_color.rgb)
			: recolored(COLOR.rgb, vec3(0.080, 0.352, 0.080), pants_color.rgb)) * tint.rgb;
		else if (kind == 1 && COLOR.b > max(COLOR.r, COLOR.g) * 1.6) base = recolored(COLOR.rgb, vec3(0.080, 0.080, 0.434), eye_color.rgb) * tint.rgb;
		else if (kind == 5) base = recolored(COLOR.rgb, vec3(0.216), shoe_color.rgb) * tint.rgb;
	}
	vec3 n = normalize(v_rest_normal);
	vec3 w = tri_weights(n);
	vec3 bump = vec3(0.0);
	float rough = roughness_base;
	float metal = metallic_base;
	vec3 emit = vec3(0.0);
	float spec = 0.5;
	if (kind == 0) {
		// Skin: pores and micro-folds; slightly oilier (smoother) where the detail is raised.
		vec4 d = rest_sample(detail_array, detail_layer, v_rest, w, detail_tile);
		bump = rest_bump(detail_array, detail_layer, v_rest, n, w, detail_tile) * detail_strength
			+ rest_bump(detail_array, detail_layer, v_rest, n, w, detail_tile * 4.0) * detail_strength * 0.5;
		// The world's grading lifts saturation by up to a fifth by day: skin starts a little greyer so it reads as
		// skin rather than orange, and the subsurface wrap below adds the warmth back in the shadows.
		base = mix(vec3(dot(base, vec3(0.2126, 0.7152, 0.0722))), base, 0.75);
		base *= 0.93 + 0.14 * d.z;
		rough = roughness_base + (0.5 - d.z) * 0.15;
		spec = 0.3;
		SSS_STRENGTH = sss;
	} else if (kind == 1) {
		spec = 0.55;
		emit = base * 0.03;
	} else if (kind == 2) {
		// Strands: fine stripes across the hair with clumped variation, darker toward the roots.
		float across = UV.y * 90.0;
		float fibre = texture(detail_array, vec3(vec2(UV.x * 0.8, across * 0.05), detail_layer)).z;
		float stripe = 0.8 + 0.2 * sin(across * 6.2831 + fibre * 6.0);
		base *= stripe * (0.85 + 0.3 * fibre);
		rough = roughness_base + (fibre - 0.5) * 0.1;
		f_tangent = normalize(TANGENT);
		f_shift = (fibre - 0.5) * 0.2 + (stripe - 0.8) * 0.3;   // per-strand jitter of the highlights
		spec = 0.2;     // the sky's reflection stays faint and broad: the strand highlights below do the shine
	} else if (kind == 8) {
		emit = base * 3.5;
	} else if (kind >= 4) {
		// Cloth, leather, metal, bone: the props texture's pattern with the vertex colour's hue.
		vec4 a = rest_sample(albedo_array, layer, v_rest, w, tile);
		vec4 m = rest_sample(material_array, layer, v_rest, w, tile);
		float lum = dot(a.rgb, vec3(0.299, 0.587, 0.114));
		base *= 0.55 + 0.9 * lum;
		bump = rest_bump(material_array, layer, v_rest, n, w, tile) * 0.8;
		rough = mix(roughness_base, m.z, 0.5);
		metal = max(metallic_base, m.w);
		if (kind == 4 && sheen > 0.0) {
			float rim = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
			emit += base * rim * sheen * strata_sky_visibility(v_light) * strata_daylight * 0.3;
		}
	}
	// Model-space perturbation to view space (the mesh arrives skinned, so model space is the posed body).
	NORMAL = normalize(NORMAL + (VIEW_MATRIX * (MODEL_MATRIX * vec4(bump, 0.0))).xyz);
	ALBEDO = base;
	ROUGHNESS = clamp(rough, 0.02, 1.0);
	METALLIC = metal;
	SPECULAR = spec;
	f_occ = COLOR.a;
	EMISSION = strata_block_light(base, v_light, f_occ) + emit * (kind == 8 ? 1.0 : f_occ);
	AO = strata_sky_visibility(v_light) * f_occ;
	f_sun_gate = strata_sun_gate(v_light) * strata_cloud_shadow(v_world, strata_light_dir);
}

void light() {
	float g = LIGHT_IS_DIRECTIONAL ? f_sun_gate : 1.0;
	// Baked occlusion reaches direct light partly (creases the shadow maps are too coarse to catch) and its
	// highlights fully.
	float occ_diffuse = mix(1.0, f_occ, 0.6);
	float occ_spec = f_occ * f_occ;
	vec3 radiance = LIGHT_COLOR * ATTENUATION * g;
	if (kind == 2) {
		// Kajiya-Kay with shifted tangents: a light-coloured primary highlight toward the tips and a broader one in
		// the hair's own colour toward the roots, both jittered per strand so they break into streaks.
		vec3 h = normalize(VIEW + LIGHT);
		vec3 t = normalize(f_tangent - NORMAL * dot(f_tangent, NORMAL));
		vec3 t1 = normalize(t + NORMAL * (-0.06 + f_shift));
		vec3 t2 = normalize(t + NORMAL * (0.14 + f_shift));
		float th1 = dot(t1, h);
		float th2 = dot(t2, h);
		float s1 = pow(sqrt(max(1.0 - th1 * th1, 0.0)), 70.0);
		float s2 = pow(sqrt(max(1.0 - th2 * th2, 0.0)), 20.0);
		float ndl = clamp(dot(NORMAL, LIGHT) * 0.6 + 0.4, 0.0, 1.0);
		DIFFUSE_LIGHT += radiance * ndl * occ_diffuse / PI;
		vec3 primary = mix(ALBEDO, vec3(1.0), 0.5) * 0.05;
		vec3 secondary = ALBEDO * 0.11;
		SPECULAR_LIGHT += radiance * clamp(dot(NORMAL, LIGHT) * 0.5 + 0.5, 0.0, 1.0) * (s1 * primary + s2 * secondary) * occ_spec;
	} else if (kind == 0) {
		// Skin: red light scatters furthest under the surface, so it wraps further past the terminator than green
		// and blue (a soft, warm shadow edge while the lit side stays neutral); GGX gives the oily sheen.
		float ndl = dot(NORMAL, LIGHT);
		vec3 wrap = vec3(0.22, 0.12, 0.09);
		vec3 diffuse = clamp((vec3(ndl) + wrap) / (1.0 + wrap), 0.0, 1.0);
		DIFFUSE_LIGHT += radiance * diffuse * occ_diffuse / PI;
		vec3 unused = vec3(0.0);
		vec3 spec = vec3(0.0);
		strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, 0.0, ALBEDO, unused, spec);
		SPECULAR_LIGHT += spec * occ_spec;
	} else {
		vec3 diffuse = vec3(0.0);
		vec3 spec = vec3(0.0);
		strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, METALLIC, ALBEDO, diffuse, spec);
		DIFFUSE_LIGHT += diffuse * occ_diffuse;
		SPECULAR_LIGHT += spec * occ_spec;
	}
}
