shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled;

// Water (1), lava (2), honey (3); the type arrives in CUSTOM0.x.
// Water refracts the scene behind it (screen texture), absorbs light with depth (Beer-Lambert), scatters its own
// colour, reflects the sky through the environment BRDF, foams where it is shallow and throws caustics on the
// ground under it. Lava is a crust of dark plates drifting over the melt, cracks and open pools glowing and pulsing. Honey is thick amber.
#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"

global uniform float strata_time;
uniform sampler2D depth_texture : hint_depth_texture, filter_linear_mipmap;
uniform sampler2D screen_texture : hint_screen_texture, filter_linear_mipmap;
uniform vec3 water_scatter : source_color = vec3(0.05, 0.2, 0.22);
// Per metre: clear water loses red first; ponds and lakes carry some silt and algae, so all channels fade a little.
uniform vec3 water_absorb = vec3(0.75, 0.24, 0.2);
uniform vec3 lava_hot : source_color = vec3(1.0, 0.55, 0.1);
uniform vec3 lava_crust : source_color = vec3(0.16, 0.05, 0.03);
uniform vec3 honey_color : source_color = vec3(0.86, 0.5, 0.06);

varying vec3 v_world_pos;
varying flat float v_type;
varying vec4 v_light;
varying float f_sun_gate;

float wave(vec2 p, float t) {
	return sin(p.x * 1.7 + t * 1.3) * 0.5 + sin(p.y * 2.3 - t * 1.1) * 0.35 + sin((p.x + p.y) * 3.1 + t * 2.0) * 0.15;
}

// Height of the small ripples: two noise layers drifting in different directions.
float ripples(vec2 p, float t) {
	float a = texture(strata_cloud_noise, p / 9.0 + vec2(t * 0.011, t * 0.006)).b;
	float b = texture(strata_cloud_noise, p / 3.7 - vec2(t * 0.017, -t * 0.013)).b;
	return a * 0.6 + b * 0.4;
}

vec3 ripple_normal(vec2 p, float t, float strength) {
	const float e = 0.06;
	float h = ripples(p, t);
	float hx = ripples(p + vec2(e, 0.0), t);
	float hz = ripples(p + vec2(0.0, e), t);
	vec2 wave_grad = vec2(wave(p + vec2(0.1, 0.0), t) - wave(p - vec2(0.1, 0.0), t), wave(p + vec2(0.0, 0.1), t) - wave(p - vec2(0.0, 0.1), t)) * 0.3;
	return normalize(vec3(-(hx - h) / e * strength - wave_grad.x, 1.0, -(hz - h) / e * strength - wave_grad.y));
}

// Bright, sharp network of light focused by the waves above a point on the bed.
float caustics(vec2 p, float t) {
	float a = texture(strata_cloud_noise, p / 2.6 + vec2(t * 0.02, t * 0.013)).g;
	float b = texture(strata_cloud_noise, p / 2.1 - vec2(t * 0.016, -t * 0.021)).g;
	return pow(clamp(min(a, b) * 1.6 - 0.35, 0.0, 1.0), 2.0) * 3.0;
}

// Crust plates: distances to the nearest and second-nearest drifting cell centres (cracks where they meet).
vec2 plates(vec2 x, float t) {
	vec2 n = floor(x), f = fract(x);
	float f1 = 8.0, f2 = 8.0;
	for (int j = -1; j <= 1; j++) {
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 o = fract(sin(vec2(dot(n + g, vec2(127.1, 311.7)), dot(n + g, vec2(269.5, 183.3)))) * 43758.5453);
			o = 0.5 + 0.4 * sin(t * 0.25 + 6.2831 * o);
			float d = length(g + o - f);
			if (d < f1) {
				f2 = f1;
				f1 = d;
			} else if (d < f2) {
				f2 = d;
			}
		}
	}
	return vec2(f1, f2);
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_type = CUSTOM0.x;
	v_light = CUSTOM1;
	if (NORMAL.y > 0.5 && CUSTOM0.x < 1.5) {
		VERTEX.y += wave(v_world_pos.xz, strata_time) * 0.03 - 0.03;
	}
}

void fragment() {
	int type = int(v_type + 0.5);
	float t = strata_time;
	vec2 p = v_world_pos.xz;
	float sky = strata_sky_visibility(v_light);
	AO = sky;
	f_sun_gate = strata_sun_gate(v_light) * strata_cloud_shadow(v_world_pos, strata_light_dir);
	if (type == 2) {
		// Plates of cooled crust drift over the melt; cracks between them and open pools glow and pulse. Falls and
		// sides run downward.
		vec3 geo_n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
		vec2 q = abs(geo_n.y) > 0.5 ? p + vec2(t * 0.05, t * 0.03) : vec2(v_world_pos.x + v_world_pos.z, v_world_pos.y * 1.5 + t * 0.4);
		float big = texture(strata_cloud_noise, q / 5.0).g;
		float fine = texture(strata_cloud_noise, q / 1.3 + 0.37).b;
		vec2 warp = (texture(strata_cloud_noise, q / 7.0 + 0.71).rg - 0.5) * 0.9;
		vec2 v = plates(q * 0.75 + warp, t);
		float edge = v.y - v.x;
		float crack = 1.0 - smoothstep(0.008, 0.05 + 0.05 * fine, edge);
		float pool = smoothstep(0.3, 0.2, big);
		float molten = max(crack, pool);
		float pulse = 0.8 + 0.2 * sin(t * 1.7 + big * 14.0);
		vec3 hot = mix(lava_hot * vec3(0.9, 0.45, 1.0), lava_hot * vec3(1.0, 1.7, 6.0), smoothstep(0.5, 1.0, molten * (0.75 + 0.35 * fine)));
		// Crust: dark basalt, glowing dull red near its cracked rims (heat from beneath).
		float rim = smoothstep(0.2, 0.0, edge) * (1.0 - molten);
		ALBEDO = lava_crust * (0.4 + 0.6 * fine) * (1.0 - molten);
		EMISSION = hot * molten * 2.6 * pulse + lava_hot * vec3(0.7, 0.3, 1.0) * (0.03 + rim * 0.5) * (1.0 - molten);
		ROUGHNESS = mix(0.92, 0.35, molten);
		ALPHA = 1.0;
		// Plates stand proud of the melt; their surfaces are rough and pitted.
		float h = (1.0 - molten) * 0.06 + fine * 0.02 * (1.0 - molten);
		vec3 dpx = dFdx(v_world_pos), dpy = dFdy(v_world_pos);
		vec3 r1 = cross(dpy, geo_n), r2 = cross(geo_n, dpx);
		float det = dot(dpx, r1);
		vec3 grad = sign(det) * (dFdx(h) * r1 + dFdy(h) * r2);
		vec3 bumped = normalize(abs(det) * geo_n - grad);
		NORMAL = normalize((VIEW_MATRIX * vec4(bumped, 0.0)).xyz);
	} else {
		vec3 geo_n = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
		vec3 world_n = FRONT_FACING ? ripple_normal(p, t, type == 3 ? 0.08 : 0.5) : vec3(0.0, -1.0, 0.0);
		if (abs(geo_n.y) < 0.5) {
			// Sides and falls: streaks running down the face.
			float streak = texture(strata_cloud_noise, vec2(v_world_pos.x + v_world_pos.z, v_world_pos.y * 0.3 + t * 0.4) / 2.0).b;
			world_n = normalize(geo_n + vec3(0.0, (streak - 0.5) * 0.6, 0.0));
		}
		vec3 n_view = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
		// What lies behind: depth difference gives the water thickness along the view ray.
		float depth = texture(depth_texture, SCREEN_UV).r;
		vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
		behind.xyz /= behind.w;
		float thickness = clamp(length(behind.xyz) - length(VERTEX), 0.0, 60.0);
		vec2 offset = n_view.xy * 0.035 * clamp(thickness, 0.0, 1.5);
		vec2 refr_uv = SCREEN_UV + offset;
		float refr_depth = texture(depth_texture, refr_uv).r;
		vec4 refr_pos = INV_PROJECTION_MATRIX * vec4(refr_uv * 2.0 - 1.0, refr_depth, 1.0);
		refr_pos.xyz /= refr_pos.w;
		if (length(refr_pos.xyz) < length(VERTEX)) refr_uv = SCREEN_UV; // never refract things in front of the surface
		vec3 refracted = textureLod(screen_texture, refr_uv, 0.0).rgb;
		float fresnel = 0.02 + 0.98 * pow(1.0 - clamp(dot(n_view, VIEW), 0.0, 1.0), 5.0);
		if (type == 3) {
			vec3 trans = exp(-vec3(0.8, 1.6, 4.5) * thickness * 2.0);
			ALBEDO = honey_color * (1.0 - trans.g) * 0.6;
			EMISSION = refracted * trans * (1.0 - fresnel) + strata_block_light(honey_color, v_light, 1.0);
			ROUGHNESS = 0.12;
		} else {
			vec3 trans = exp(-water_absorb * thickness);
			// Light focused by the surface dances on the bed (only where the sun reaches).
			vec3 bed = (INV_VIEW_MATRIX * vec4(behind.xyz, 1.0)).xyz;
			float focus = caustics(bed.xz, t) * f_sun_gate * strata_daylight * clamp(thickness * 1.5, 0.0, 1.0) * trans.g;
			refracted *= 1.0 + focus;
			float foam_edge = 1.0 - smoothstep(0.0, 0.35, thickness);
			float foam_tex = texture(strata_cloud_noise, p / 1.6 + vec2(t * 0.02, 0.0)).b;
			float foam = clamp(foam_edge * smoothstep(0.35, 0.7, foam_tex + foam_edge * 0.4), 0.0, 1.0) * float(FRONT_FACING);
			ALBEDO = mix(water_scatter * (1.0 - trans.g), vec3(0.9), foam);
			EMISSION = refracted * trans * (1.0 - fresnel) * (1.0 - foam) + strata_block_light(water_scatter, v_light, 1.0) * 0.5;
			ROUGHNESS = mix(0.03, 0.8, foam);
		}
		ALPHA = 1.0;
		SPECULAR = 0.5;
		NORMAL = n_view;
	}
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, 0.0, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
