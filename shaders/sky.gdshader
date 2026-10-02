shader_type sky;
render_mode use_quarter_res_pass;

// Physically based sky: single-scattering atmosphere (Rayleigh, Mie, ozone) lit by the sun and the moon, a cloud
// layer with light marching, sun and moon discs (limb darkening, lunar phases), stars and the galactic band.
// The atmosphere runs in the quarter-resolution pass; the radiance cubemap (ambient light and reflections) gets a
// cheaper evaluation with the biome's ambient tint and no discs or stars.
#include "res://shaders/include/clouds.gdshaderinc"

uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
uniform vec3 moon_dir = vec3(0.0, -1.0, 0.0);
// 0 full, 0.5 new, 1 full again.
uniform float moon_phase = 0.0;
uniform float sun_intensity = 33.0;
uniform float moon_intensity = 0.8;
// Direct sun/moon colour reaching the ground (atmospheric transmittance times energy), lights the clouds.
uniform vec3 sun_light = vec3(1.0);
uniform vec3 moon_light = vec3(0.1);
// Average sky irradiance colour (ambient on the clouds' shaded sides).
uniform vec3 sky_ambient = vec3(0.3, 0.4, 0.6);
// Per-frame values are globals: changing a sky material parameter re-renders the radiance cubemap, so the material
// itself only changes a few times a second (EnvironmentController).
global uniform vec3 strata_camera;
global uniform float strata_time;
uniform vec3 tint : source_color = vec3(1.0);
uniform vec3 ambient_tint : source_color = vec3(1.0);
uniform vec3 airglow = vec3(0.0035, 0.0055, 0.011);
uniform float night = 0.0;
uniform float star_rotation = 0.0;
uniform int cloud_steps = 3;
// Rain clouds (0 clear .. 1 a dark downpour sky): the whole sky turns a heavy grey, the sun and stars are hidden.
uniform float overcast = 0.0;

vec3 overcast_sky(vec3 col) {
	float lum = dot(col, vec3(0.2126, 0.7152, 0.0722));
	vec3 grey = vec3(lum) * vec3(0.92, 0.96, 1.04);
	return mix(col, grey * 0.55, overcast * 0.85);
}

const float RP = 6360.0;
const float RA = 6420.0;
const vec3 BETA_R = vec3(5.802e-3, 13.558e-3, 33.1e-3);
const float BETA_M = 3.996e-3;
const float BETA_M_EXT = 4.4e-3;
const vec3 BETA_O = vec3(0.650e-3, 1.881e-3, 0.085e-3);

vec3 densities(float h) {
	return vec3(exp(-h / 8.0), exp(-h / 1.2), max(0.0, 1.0 - abs(h - 25.0) / 15.0));
}

float ray_top(vec3 o, vec3 d) {
	float b = dot(o, d);
	float c = dot(o, o) - RA * RA;
	return -b + sqrt(max(b * b - c, 0.0));
}

bool hits_planet(vec3 o, vec3 d) {
	float b = dot(o, d);
	return b < 0.0 && b * b - (dot(o, o) - RP * RP) > 0.0;
}

// In-scattered radiance along `d` for light arriving from `l` (unit vectors), `n` view samples, `m` light samples.
vec3 atmosphere(vec3 d, vec3 l, float intensity, int n, int m) {
	if (intensity <= 0.0 || l.y < -0.3) return vec3(0.0);
	vec3 o = vec3(0.0, RP + 0.2, 0.0);
	float tmax = ray_top(o, d);
	vec3 od = vec3(0.0);
	vec3 sr = vec3(0.0);
	vec3 sm = vec3(0.0);
	for (int i = 0; i < n; i++) {
		float fi = float(i) / float(n);
		float fj = float(i + 1) / float(n);
		float t0 = tmax * fi * fi;
		float t1 = tmax * fj * fj;
		float ds = t1 - t0;
		vec3 p = o + d * (0.5 * (t0 + t1));
		vec3 rho = densities(length(p) - RP) * ds;
		od += rho;
		if (hits_planet(p, l)) continue;
		float dl = ray_top(p, l) / float(m);
		vec3 lod = vec3(0.0);
		for (int j = 0; j < m; j++) lod += densities(max(length(p + l * ((float(j) + 0.5) * dl)) - RP, 0.0));
		vec3 tau = BETA_R * (od.x + lod.x * dl) + BETA_M_EXT * (od.y + lod.y * dl) + BETA_O * (od.z + lod.z * dl);
		vec3 tr = exp(-tau);
		sr += rho.x * tr;
		sm += rho.y * tr;
	}
	float mu = dot(d, l);
	float pr = 3.0 / (16.0 * PI) * (1.0 + mu * mu);
	const float g = 0.76;
	float pm = 3.0 / (8.0 * PI) * ((1.0 - g * g) * (1.0 + mu * mu)) / ((2.0 + g * g) * pow(1.0 + g * g - 2.0 * g * mu, 1.5));
	return intensity * (sr * BETA_R * pr + sm * BETA_M * pm);
}

vec3 sky_color(vec3 d, int n, int m) {
	vec3 up = normalize(vec3(d.x, max(d.y, 0.002), d.z));
	vec3 col = atmosphere(up, sun_dir, sun_intensity, n, m);
	col += atmosphere(up, moon_dir, moon_intensity, max(n / 2, 4), max(m / 2, 2));
	col += airglow * (1.0 + 1.5 * exp(-max(d.y, 0.0) * 8.0));
	// Below the horizon: the haze darkens toward the unseen ground.
	return mix(col, col * 0.3, smoothstep(0.0, 0.25, -d.y));
}

float hg(float mu, float g) {
	return (1.0 - g * g) / (4.0 * PI * pow(1.0 + g * g - 2.0 * g * mu, 1.5));
}

vec4 cloud_layer(vec3 d, float detail, int steps) {
	float rise = strata_clouds.w - strata_camera.y;
	if (d.y < 0.012 || rise <= 0.0 || strata_clouds.z <= 0.001) return vec4(0.0);
	float t = rise / d.y;
	vec2 xz = strata_camera.xz + d.xz * t;
	float dens = strata_cloud_density(xz, detail);
	if (dens < 0.002) return vec4(0.0);
	bool by_sun = sun_dir.y > -0.04;
	vec3 l = by_sun ? sun_dir : moon_dir;
	vec3 lc = by_sun ? sun_light : moon_light;
	// Self shadowing: density marched toward the light across the layer (clouds are ~600 m thick).
	vec2 stride = normalize(l.xz + vec2(1e-4)) * min(600.0 / max(l.y, 0.08), 3000.0) / float(max(steps, 1));
	float od = dens;
	for (int k = 1; k <= steps; k++) od += strata_cloud_density(xz + stride * float(k), 0.4);
	float tr = exp(-od * 1.6);
	float powder = 1.0 - exp(-dens * 4.0);
	float mu = dot(d, l);
	float phase = mix(hg(mu, 0.6), hg(mu, -0.2), 0.35) * 4.0 * PI;
	vec3 col = lc * (tr * powder * phase * 1.1 + 0.08 * tr) + sky_ambient * mix(1.1, 0.55, dens);
	float fade = exp(-t / 60000.0) * smoothstep(0.012, 0.09, d.y);
	return vec4(col, clamp(dens * 1.15, 0.0, 1.0) * fade);
}

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

vec3 star_field(vec3 d) {
	float c = cos(star_rotation);
	float s = sin(star_rotation);
	// Sky rotates about a tilted pole.
	vec3 axis = normalize(vec3(0.0, 0.82, 0.57));
	vec3 r = d * c + cross(axis, d) * s + axis * dot(axis, d) * (1.0 - c);
	const float cells = 220.0;
	vec3 q = r * cells;
	vec3 cell = floor(q);
	float h = hash13(cell);
	vec3 col = vec3(0.0);
	if (h > 0.985) {
		vec3 jitter = vec3(hash13(cell + 7.1), hash13(cell + 3.7), hash13(cell + 1.3)) * 0.6 + 0.2;
		vec3 f = fract(q) - jitter;
		float mag = pow(hash13(cell + 11.0), 6.0) * 6.0 + 0.25;
		float temp = hash13(cell + 5.0);
		vec3 tone = mix(vec3(1.0, 0.78, 0.55), vec3(0.72, 0.82, 1.0), temp);
		float tw = 0.75 + 0.25 * sin(strata_time * (3.0 + temp * 5.0) + h * 400.0);
		col += tone * mag * tw * exp(-dot(f, f) * 90.0);
	}
	// Galactic band.
	vec3 pole = normalize(vec3(0.35, 0.55, 0.76));
	float b = dot(r, pole);
	vec2 uv = vec2(atan(r.x, r.z) / (2.0 * PI), r.y * 0.5 + 0.5);
	float dust = texture(strata_cloud_noise, uv * vec2(2.0, 1.0)).g;
	float band = exp(-b * b * 22.0) * (0.35 + 0.65 * smoothstep(0.35, 0.8, dust));
	col += vec3(0.55, 0.6, 0.8) * band * 0.035;
	return col;
}

vec3 sun_disc(vec3 d) {
	const float radius = 0.0105;
	float a = acos(clamp(dot(d, sun_dir), -1.0, 1.0));
	if (a > radius * 1.05) return vec3(0.0);
	float u = clamp(a / radius, 0.0, 1.0);
	float limb = 1.0 - 0.6 * (1.0 - sqrt(max(1.0 - u * u, 0.0)));
	return sun_light * 60.0 * limb * smoothstep(1.05, 0.95, a / radius);
}

vec3 moon_disc(vec3 d) {
	const float radius = 0.02;
	float md = dot(d, moon_dir);
	if (md < cos(radius * 1.2)) return vec3(0.0);
	vec3 right = normalize(cross(moon_dir, vec3(0.0, 1.0, 0.0)) + vec3(1e-4, 0.0, 0.0));
	vec3 up = cross(right, moon_dir);
	vec3 off = d - moon_dir * md;
	float x = dot(off, right) / sin(radius);
	float y = dot(off, up) / sin(radius);
	float rr = x * x + y * y;
	float halo = pow(max(md, 0.0), 900.0) * 0.08;
	if (rr > 1.0) return moon_light * halo;
	vec3 n = x * right + y * up - sqrt(1.0 - rr) * moon_dir;
	float ph = moon_phase * 2.0 * PI;
	vec3 l = -moon_dir * cos(ph) + right * sin(ph);
	float maria = texture(strata_cloud_noise, vec2(x, y) * 0.18 + 0.4).r;
	float albedo = mix(0.55, 1.0, smoothstep(0.35, 0.65, maria));
	float lit = max(dot(n, l), 0.0) + 0.015;
	float edge = smoothstep(1.0, 0.94, rr);
	return vec3(0.95, 0.96, 1.0) * albedo * lit * 3.2 * edge + moon_light * halo;
}

void sky() {
	vec3 d = normalize(EYEDIR);
	if (AT_CUBEMAP_PASS) {
		vec3 col = sky_color(d, 8, 3);
		vec4 cl = cloud_layer(d, 0.0, 1);
		col = overcast_sky(mix(col, cl.rgb, cl.a));
		COLOR = col * tint * ambient_tint;
	} else if (AT_QUARTER_RES_PASS) {
		COLOR = sky_color(d, 16, 6);
		ALPHA = 1.0;
	} else {
		vec3 col = QUARTER_RES_COLOR.rgb;
		float above = smoothstep(-0.01, 0.03, d.y);
		// Stars and the discs sit behind the air: dimmed by the airmass near the horizon.
		float airmass = exp(-0.12 / max(d.y, 0.02));
		float clear = 1.0 - overcast;
		col += star_field(d) * night * airmass * above * clear;
		col += moon_disc(d) * airmass * above * clear;
		col += sun_disc(d) * above * clear;
		vec4 cl = cloud_layer(d, 1.0, cloud_steps);
		col = overcast_sky(mix(col, cl.rgb, cl.a));
		COLOR = col * tint;
	}
}
