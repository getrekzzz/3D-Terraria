shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

// Lava, drawn opaque in its own pass (no screen copy). A slowly churning temperature field decides where the melt is
// open and where it has skinned over: cooler rafts carry plates of dark basalt crust whose cracks glow, hot rivers run
// bare in blackbody colours (deep red → orange → yellow → near white), bubbles swell under a skin and pop, and the
// surface heaves gently. Falls and sides stream downward. Everything molten is HDR emission, so the glow pass blooms
// it; the crust is lit like rock, including by the lava's own baked light.
#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"
#include "res://shaders/include/noise.gdshaderinc"

global uniform float strata_time;
uniform vec3 crust_color : source_color = vec3(0.07, 0.048, 0.042);
uniform float glow = 1.6;

varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying vec4 v_light;
varying float f_sun_gate;

// Blackbody-like colour of a 0..1 temperature: most molten lava sits in the orange; yellow-white only where hottest.
vec3 heat_color(float t) {
	vec3 c = mix(vec3(0.16, 0.008, 0.0), vec3(0.78, 0.07, 0.008), smoothstep(0.0, 0.3, t));
	c = mix(c, vec3(1.0, 0.27, 0.025), smoothstep(0.28, 0.55, t));
	c = mix(c, vec3(1.0, 0.52, 0.09), smoothstep(0.55, 0.8, t));
	return mix(c, vec3(1.0, 0.84, 0.5), smoothstep(0.82, 1.0, t));
}

// Brightness of a 0..1 temperature (radiance climbs steeply with heat).
float heat_power(float t) {
	return 0.08 + pow(t, 2.5) * 1.7;
}

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_light = CUSTOM1;
	if (NORMAL.y > 0.5) {
		// A slow heave; only ever downward so the surface never lifts off the sides of its pool.
		float swell = strata_fbm3(v_world_pos.xz * 0.35 + vec2(strata_time * 0.05, -strata_time * 0.04));
		VERTEX.y -= 0.015 + swell * 0.045;
	}
}

void fragment() {
	float t = strata_time;
	float sky = strata_sky_visibility(v_light);
	AO = sky;
	f_sun_gate = strata_sun_gate(v_light) * strata_cloud_shadow(v_world_pos, strata_light_dir);
	vec3 n = normalize(v_world_normal);
	float heat;
	float crust;
	float rim;
	float bump;
	if (n.y > 0.5) {
		vec2 p = v_world_pos.xz;
		// Convection: the pattern wanders back and forth (bounded, so it never smears out over a long session).
		vec2 drift = vec2(sin(t * 0.021), cos(t * 0.017)) * 1.8 + vec2(sin(t * 0.053 + p.y * 0.1), cos(t * 0.047 + p.x * 0.1)) * 0.35;
		vec2 q = p + drift;
		// Temperature: broad hot rivers and cool rafts, slowly shifting; most of a lake is crusted over.
		float temp = strata_fbm4(q * 0.13 + vec2(t * 0.012, -t * 0.009));
		temp = clamp(mix(temp, strata_fbm3(q * 0.55 - t * 0.02), 0.3) * 1.35 - 0.3, 0.0, 1.0);
		float cool = smoothstep(0.15, 0.6, 1.0 - temp);
		// Plates at two scales, strongly warped so none are alike: big rafts split by glowing seams, each raft
		// crazed by finer cooling fissures.
		vec2 warp = (vec2(strata_fbm3(q * 0.35), strata_fbm3(q * 0.35 + 5.2)) - 0.5) * 2.2;
		vec3 big = strata_cells(q * 0.42 + warp, t * 0.08);
		vec3 small = strata_cells(q * 1.25 + warp * 1.7 + 3.1, t * 0.1);
		float px = length(fwidth(q)); // widen seams where they would alias into shimmer
		float seam_w = mix(0.3, 0.07, cool) + px * 0.5;
		float seam = 1.0 - smoothstep(seam_w * 0.3, seam_w, big.y - big.x);
		float fissure = (1.0 - smoothstep(0.02 + px * 1.2, 0.06 + px * 1.5, small.y - small.x)) * cool;
		float cover = smoothstep(0.1, 0.45, cool + (big.z - 0.5) * 0.35);
		crust = cover * (1.0 - seam) * (1.0 - fissure * 0.85);
		rim = cover * smoothstep(seam_w * 2.8, seam_w * 0.6, big.y - big.x) * (1.0 - seam);
		// Bubbles in the open melt: a skin swells, darkens, then bursts in a bright ring.
		vec3 b = strata_cells(p * 1.7 + 13.1, 0.0);
		float life = fract(t * 0.22 + b.z * 17.0);
		float bubbling = step(0.7, fract(b.z * 91.7)) * (1.0 - cover);
		float r = 0.08 + life * 0.3;
		float dome = (1.0 - smoothstep(r * 0.6, r, b.x)) * bubbling * smoothstep(0.1, 0.8, life) * (1.0 - step(0.93, life));
		float burst = smoothstep(0.08, 0.0, abs(b.x - (life - 0.93) * 6.0)) * bubbling * step(0.93, life);
		float melt = 0.5 + 0.38 * temp;
		heat = mix(melt, 0.62 + 0.2 * temp, seam * cover);
		heat = mix(heat, 0.45, fissure * cover * (1.0 - seam));
		heat = mix(heat, heat * 0.75, dome) + burst * 0.35;
		heat *= 0.94 + 0.06 * sin(t * 0.9 + temp * 11.0 + big.z * 6.0);
		crust = max(crust, dome * 0.35);
		bump = crust * (0.035 + 0.015 * strata_value_noise(p * 9.0)) - fissure * 0.01 + dome * 0.03;
	} else if (n.y < -0.5) {
		// The underside of a hanging drip: dim and crusted.
		heat = 0.3;
		crust = 0.6;
		rim = 0.2;
		bump = 0.0;
	} else {
		// Falls and sides stream downward, crust streaks carried along in the flow.
		vec2 s = vec2((v_world_pos.x + v_world_pos.z) * 2.2, v_world_pos.y * 0.55 + t * 0.9);
		float streak = strata_fbm3(s);
		float fast = strata_value_noise(vec2(s.x * 2.0, s.y * 3.0 + t * 1.5));
		crust = smoothstep(0.62, 0.8, streak) * 0.8;
		heat = mix(0.65 + 0.3 * fast, 0.35, crust);
		rim = crust * 0.4;
		bump = crust * 0.02;
	}
	vec3 rock = crust_color * (0.6 + 0.7 * strata_value_noise(v_world_pos.xz * 13.0 + v_world_pos.y * 7.0));
	ALBEDO = mix(vec3(0.03, 0.01, 0.004), rock, crust);
	ROUGHNESS = mix(0.25, 0.9, crust);
	SPECULAR = 0.45;
	vec3 molten = heat_color(heat) * heat_power(heat) * glow * (1.0 - crust);
	vec3 embers = heat_color(0.28) * rim * 0.35; // crust edges still glowing dull red
	EMISSION = molten + embers + strata_block_light(ALBEDO, v_light, 1.0);
	// Surface-gradient bump: plates and bubble skins stand proud of the melt.
	vec3 dpx = dFdx(v_world_pos);
	vec3 dpy = dFdy(v_world_pos);
	vec3 r1 = cross(dpy, n);
	vec3 r2 = cross(n, dpx);
	float det = dot(dpx, r1);
	vec3 grad = sign(det) * (dFdx(bump) * r1 + dFdy(bump) * r2);
	vec3 bumped = normalize(abs(det) * n - grad);
	NORMAL = normalize((VIEW_MATRIX * vec4(bumped, 0.0)).xyz);
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, 0.0, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
