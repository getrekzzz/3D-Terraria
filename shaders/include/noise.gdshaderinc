// Small procedural noise helpers for effects (flames, lava): cheap hash-based value noise and fbm, no textures.

float strata_hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

vec2 strata_hash22(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * vec3(0.1031, 0.1030, 0.0973));
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.xx + p3.yz) * p3.zy);
}

// Smooth value noise in 0..1.
float strata_value_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = strata_hash12(i);
	float b = strata_hash12(i + vec2(1.0, 0.0));
	float c = strata_hash12(i + vec2(0.0, 1.0));
	float d = strata_hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// Three octaves of value noise (0..1).
float strata_fbm3(vec2 p) {
	float v = 0.5 * strata_value_noise(p);
	v += 0.25 * strata_value_noise(p * 2.03 + 17.1);
	v += 0.125 * strata_value_noise(p * 4.01 + 41.7);
	return v / 0.875;
}

// Four octaves with rotation between octaves (0..1): richer, for large surfaces.
float strata_fbm4(vec2 p) {
	mat2 r = mat2(vec2(0.8, 0.6), vec2(-0.6, 0.8));
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += a * strata_value_noise(p);
		p = r * p * 2.02 + 11.3;
		a *= 0.5;
	}
	return v / 0.9375;
}

// Cellular noise: x = distance to the nearest feature point, y = to the second nearest, z = id of the nearest cell.
vec3 strata_cells(vec2 p, float t) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float d1 = 8.0;
	float d2 = 8.0;
	float id = 0.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 g = vec2(float(x), float(y));
			vec2 h = strata_hash22(i + g);
			vec2 o = g + 0.5 + 0.38 * sin(t + 6.2831 * h) - f;
			float d = dot(o, o);
			if (d < d1) {
				d2 = d1;
				d1 = d;
				id = strata_hash12(i + g);
			} else if (d < d2) {
				d2 = d;
			}
		}
	}
	return vec3(sqrt(d1), sqrt(d2), id);
}
