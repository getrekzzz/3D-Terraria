// The block cursor's faces: a faint tinted fill drawn over the terrain (it shows the grid cube even where smooth ground
// bulges over it) and cracks that spread across the faces as mining progresses.
// UV runs 0–1 across each face (cracks spread from its middle); UV2 is the face position in metres (crack size).
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, depth_test_disabled, cull_back, shadows_disabled;

uniform vec4 fill_color : source_color = vec4(1.0, 1.0, 1.0, 0.1);
uniform float progress = 0.0;

vec2 hash2(vec2 p) {
	p = vec2(dot(p, vec2(127.1, 311.7)), dot(p, vec2(269.5, 183.3)));
	return fract(sin(p) * 43758.5453);
}

// Distance to the nearest Voronoi cell border: thin lines where two cells meet.
float voronoi_edge(vec2 p) {
	vec2 n = floor(p);
	vec2 f = fract(p);
	vec2 mr = vec2(0.0);
	float md = 8.0;
	for (int j = -1; j <= 1; j++)
		for (int i = -1; i <= 1; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 r = g + hash2(n + g) - f;
			float d = dot(r, r);
			if (d < md) { md = d; mr = r; }
		}
	md = 8.0;
	for (int j = -2; j <= 2; j++)
		for (int i = -2; i <= 2; i++) {
			vec2 g = vec2(float(i), float(j));
			vec2 r = g + hash2(n + g) - f;
			if (dot(mr - r, mr - r) > 0.00001) md = min(md, dot(0.5 * (mr + r), normalize(r - mr)));
		}
	return md;
}

void fragment() {
	float alpha = fill_color.a;
	vec3 color = fill_color.rgb;
	if (progress > 0.0) {
		// Cracks: coarse cells first, finer ones join as the block weakens; lines widen towards breaking.
		float coarse = voronoi_edge(UV2 * 3.2);
		float fine = voronoi_edge(UV2 * 7.5 + 13.1);
		float width = 0.02 + 0.06 * progress;
		float crack = 1.0 - smoothstep(width * 0.5, width, coarse);
		crack = max(crack, (1.0 - smoothstep(width * 0.4, width * 0.8, fine)) * smoothstep(0.35, 0.8, progress));
		// Cracks grow outward from the centre of each face.
		vec2 c = UV - 0.5;
		crack *= step(length(c) * 1.2, progress * 1.4 + 0.15);
		color = mix(color, vec3(0.05, 0.04, 0.03), crack);
		alpha = max(alpha + 0.12 * progress, crack * (0.55 + 0.35 * progress));
	}
	ALBEDO = color;
	ALPHA = alpha;
}
