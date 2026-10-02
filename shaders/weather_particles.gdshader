// Weather particles (rain streaks, snow, blowing sand, rain splashes) in a box that follows the camera. Particles are
// born at random heights in the box (fading in) and die where they meet the sky map: the top of the first blocking
// cell of their column, uploaded as a small height texture around the camera (no rain in caves or under roofs, and
// no per-particle raycasts). Splashes are born on those tops. Horizontal drift wraps around the camera.
shader_type particles;

// 0 rain, 1 snow, 2 sand, 3 splash.
uniform int kind = 0;
uniform sampler2D sky_heights : filter_nearest, repeat_disable;
// x, z of the texture's corner (m), 1 / its size (m), and a height that means "no data".
uniform vec4 sky_rect = vec4(0.0, 0.0, 0.02, -1.0e6);
uniform vec3 wind = vec3(0.0);
uniform float fall_speed = 9.0;
uniform float box_radius = 12.0;
uniform float box_top = 11.0;
uniform float box_height = 17.0;

float hash(uint x) {
	x ^= x >> 16u;
	x *= 0x7feb352du;
	x ^= x >> 15u;
	x *= 0x846ca68bu;
	x ^= x >> 16u;
	return float(x & 0xffffffu) / 16777215.0;
}

float sky_top(vec2 xz) {
	vec2 uv = (xz - sky_rect.xy) * sky_rect.z;
	if (uv.x < 0.0 || uv.y < 0.0 || uv.x >= 1.0 || uv.y >= 1.0) return sky_rect.w;
	return texture(sky_heights, uv).r;
}

void start() {
	uint s = RANDOM_SEED + uint(INDEX) * 7919u + uint(NUMBER) * 104729u;
	vec3 c = EMISSION_TRANSFORM[3].xyz;
	vec2 off = (vec2(hash(s), hash(s + 1u)) * 2.0 - 1.0) * box_radius;
	vec3 p = vec3(c.x + off.x, 0.0, c.z + off.y);
	if (kind == 3) {
		// A splash lands where the rain does: on the column's top, near enough to the eye to be seen.
		off *= 0.6;
		p.xz = c.xz + off;
		p.y = sky_top(p.xz) + 0.03;
		if (abs(p.y - c.y) > 10.0) ACTIVE = false;
		VELOCITY = vec3(0.0);
	} else {
		p.y = c.y + box_top - hash(s + 2u) * box_height;
		float jitter = 0.85 + 0.3 * hash(s + 3u);
		VELOCITY = vec3(wind.x, -fall_speed * jitter, wind.z);
	}
	TRANSFORM = mat4(1.0);
	TRANSFORM[3].xyz = p;
	CUSTOM = vec4(hash(s + 4u), 0.0, hash(s + 5u), 0.0);
	COLOR = vec4(1.0, 1.0, 1.0, 0.0);
}

void process() {
	CUSTOM.y += DELTA / max(LIFETIME, 0.001);
	float age = CUSTOM.y;
	vec3 c = EMISSION_TRANSFORM[3].xyz;
	if (kind == 3) {
		// A crown of droplets that spreads and fades fast.
		float sz = mix(0.25, 0.6, age) * (0.7 + 0.6 * CUSTOM.x);
		TRANSFORM[0].xyz = vec3(sz, 0.0, 0.0);
		TRANSFORM[1].xyz = vec3(0.0, sz, 0.0);
		TRANSFORM[2].xyz = vec3(0.0, 0.0, sz);
		COLOR.a = (1.0 - age) * smoothstep(0.0, 0.1, age);
	} else {
		if (kind == 1) {
			// Flakes tumble and flutter across the wind.
			float t = TIME * (0.8 + CUSTOM.x) + CUSTOM.z * 40.0;
			VELOCITY.xz = wind.xz + vec2(sin(t * 1.3), cos(t * 0.9)) * 0.7;
		} else if (kind == 2) {
			// Sand streams with the wind, rising and dipping in eddies.
			float t = TIME * 1.7 + CUSTOM.z * 30.0;
			VELOCITY.y = sin(t) * 1.2 - 0.4;
		}
		vec3 p = TRANSFORM[3].xyz;
		vec2 d = p.xz - c.xz;
		if (abs(d.x) > box_radius) p.x -= sign(d.x) * 2.0 * box_radius;
		if (abs(d.y) > box_radius) p.z -= sign(d.y) * 2.0 * box_radius;
		if (p.y > c.y + box_top + 2.0) p.y -= box_height;
		if (p.y < c.y + box_top - box_height - 4.0) ACTIVE = false;
		if (p.y < sky_top(p.xz)) ACTIVE = false; // met a roof, the ground or a cave ceiling
		TRANSFORM[3].xyz = p;
		if (kind == 0 || kind == 2) {
			// Streaks lie along their motion (rain falling, sand driven by the wind).
			vec3 axis = normalize(VELOCITY);
			vec3 side = normalize(cross(axis, abs(axis.z) < 0.95 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0)));
			TRANSFORM[0].xyz = side;
			TRANSFORM[1].xyz = axis;
			TRANSFORM[2].xyz = cross(side, axis);
		}
		COLOR.a = smoothstep(0.0, 0.12, age) * (1.0 - smoothstep(0.85, 1.0, age));
	}
}
