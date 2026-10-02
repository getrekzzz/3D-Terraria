// How a weather particle looks: rain and blowing sand are thin streaks billboarded around their motion; snow flakes and
// splashes are soft camera-facing sprites. Unshaded but lit by the sky ambient (dim at night) and lightning flashes.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform, fog_disabled;

global uniform vec4 strata_sky_ambient;
global uniform float strata_daylight;
global uniform vec4 strata_weather;

// 0 rain, 1 snow, 2 sand, 3 splash.
uniform int kind = 0;
uniform vec4 tint : source_color = vec4(0.72, 0.78, 0.86, 1.0);
uniform vec2 size = vec2(0.012, 0.55);
uniform float opacity = 0.35;

void vertex() {
	vec3 center = MODEL_MATRIX[3].xyz;
	vec3 world;
	if (kind == 0 || kind == 2) {
		vec3 axis = normalize(MODEL_MATRIX[1].xyz);
		vec3 to_cam = normalize(INV_VIEW_MATRIX[3].xyz - center);
		vec3 side = normalize(cross(axis, to_cam));
		world = center + side * VERTEX.x * size.x + axis * VERTEX.y * size.y;
	} else {
		float s = length(MODEL_MATRIX[0].xyz);
		// Splashes stand on the surface they hit (their quad's bottom at the particle); the rest are centred sprites.
		vec3 up = kind == 3 ? vec3(0.0, 1.0, 0.0) : INV_VIEW_MATRIX[1].xyz;
		float lift = kind == 3 ? 1.0 : 0.0;
		world = center + (INV_VIEW_MATRIX[0].xyz * VERTEX.x * size.x + up * (VERTEX.y + lift) * size.y) * s;
	}
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
	NORMAL = vec3(0.0, 0.0, 1.0);
}

void fragment() {
	vec2 q = UV * 2.0 - 1.0;
	float shape;
	if (kind == 0 || kind == 2) {
		shape = (1.0 - q.x * q.x) * smoothstep(1.0, 0.2, abs(q.y));
	} else if (kind == 3) {
		// A low crown of spray: wide at the base, thinning upward, open in the middle.
		float y = q.y * 0.5 + 0.5;
		float crown = exp(-q.x * q.x * 5.0) * (1.0 - smoothstep(0.0, 0.9, y)) * smoothstep(-0.05, 0.12, y);
		float mist = exp(-(q.x * q.x * 2.5 + (y - 0.1) * (y - 0.1) * 30.0)) * 0.5;
		shape = max(crown * (0.5 + 0.5 * abs(q.x * 3.0)), mist);
	} else {
		shape = max(0.0, 1.0 - dot(q, q));
		shape *= shape;
	}
	vec3 sky = strata_sky_ambient.rgb;
	float lum = max(0.06, dot(sky, vec3(0.3, 0.5, 0.2)));
	vec3 light = tint.rgb * (0.35 + 1.6 * lum) + vec3(0.8, 0.85, 1.0) * strata_weather.z * 1.5;
	ALBEDO = light;
	ALPHA = clamp(shape * COLOR.a * opacity, 0.0, 1.0);
}
