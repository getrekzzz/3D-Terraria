shader_type sky;

// Procedural day/night sky: gradient, sun disc, moon with phases, stars, horizon haze.
uniform vec3 day_top : source_color = vec3(0.26, 0.47, 0.82);
uniform vec3 day_horizon : source_color = vec3(0.72, 0.84, 0.95);
uniform vec3 dusk_horizon : source_color = vec3(0.98, 0.52, 0.28);
uniform vec3 night_top : source_color = vec3(0.01, 0.015, 0.04);
uniform vec3 night_horizon : source_color = vec3(0.04, 0.06, 0.12);
uniform float daylight = 1.0;
uniform float dusk = 0.0;
uniform vec3 sun_dir = vec3(0.0, 1.0, 0.0);
uniform vec3 moon_dir = vec3(0.0, -1.0, 0.0);
uniform float moon_phase = 0.0;
uniform vec3 tint : source_color = vec3(1.0);

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

void sky() {
	vec3 d = normalize(EYEDIR);
	float h = clamp(d.y, -1.0, 1.0);
	float up = pow(clamp(h, 0.0, 1.0), 0.45);
	vec3 day = mix(day_horizon, day_top, up);
	vec3 night = mix(night_horizon, night_top, up);
	vec3 col = mix(night, day, daylight);
	float horizon_band = exp(-abs(h) * 6.0);
	col = mix(col, dusk_horizon, dusk * horizon_band * 0.85);

	float sun = smoothstep(0.9993, 0.9997, dot(d, sun_dir));
	float sun_glow = pow(max(dot(d, sun_dir), 0.0), 64.0) * 0.6 + pow(max(dot(d, sun_dir), 0.0), 8.0) * 0.15 * dusk;
	col += vec3(1.0, 0.92, 0.75) * (sun * 18.0 + sun_glow) * step(0.0, sun_dir.y + 0.1);

	float md = dot(d, moon_dir);
	float moon = smoothstep(0.9990, 0.9994, md);
	vec3 side = normalize(cross(moon_dir, vec3(0.0, 1.0, 0.0)) + vec3(0.0001));
	float lit = smoothstep(-0.2, 0.2, dot(d - moon_dir, side) * 400.0 + cos(moon_phase * 6.2831853) * 12.0);
	col += vec3(0.85, 0.88, 1.0) * moon * mix(0.15, 3.0, lit) * (1.0 - daylight);

	if (h > 0.0) {
		vec3 cell = floor(d * 400.0);
		float star = step(0.9975, hash(cell)) * (1.0 - daylight) * smoothstep(0.0, 0.3, h);
		col += vec3(star * (0.6 + hash(cell + 3.1)));
	}
	if (h < 0.0) col = mix(col, night_horizon * 0.5 + day_horizon * 0.3 * daylight, clamp(-h * 4.0, 0.0, 1.0));
	COLOR = col * tint;
}
