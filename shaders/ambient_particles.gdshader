// Ambient particles around the camera: dust motes hanging in the air, glowing mushroom spores, underworld embers,
// falling leaves and night fireflies. Born in a box around the eye; leaves only under a canopy or overhang and
// fireflies in the open, and wrapped around the camera
// as it moves so the air stays full without respawning. Leaves fall from the canopies of the trees nearest the camera
// (found on the CPU a couple of times a second); fireflies only dance in the open (above the sky map heights).
shader_type particles;

// 0 dust, 1 spores, 2 embers, 3 leaves, 4 fireflies.
uniform int kind = 0;
uniform sampler2D sky_heights : filter_nearest, repeat_disable;
uniform vec4 sky_rect = vec4(0.0, 0.0, 0.02, -1.0e6);
uniform float radius = 9.0;
uniform float half_height = 5.0;
// Tree canopies near the camera: xyz centre (m), w radius (m).
uniform vec4 canopies[8];
uniform int canopy_count = 0;

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
	vec3 p = c + (vec3(hash(s), hash(s + 1u), hash(s + 2u)) * 2.0 - 1.0) * vec3(radius, half_height, radius);
	float top = sky_top(p.xz);
	if (kind == 3) {
		if (canopy_count <= 0) {
			ACTIVE = false;
		} else {
			vec4 tree = canopies[min(int(hash(s + 6u) * float(canopy_count)), canopy_count - 1)];
			vec3 off = vec3(hash(s), hash(s + 1u), hash(s + 2u)) * 2.0 - 1.0;
			p = tree.xyz + off * vec3(tree.w, tree.w * 0.5, tree.w);
		}
	} else if (kind == 4) {
		p.y = c.y - 1.0 + hash(s + 2u) * 3.5;
		if (p.y < top) ACTIVE = false; // fireflies dance in the open air
	}
	TRANSFORM = mat4(1.0);
	TRANSFORM[3].xyz = p;
	VELOCITY = vec3(0.0);
	CUSTOM = vec4(hash(s + 3u), 0.0, hash(s + 4u), hash(s + 5u));
	COLOR = vec4(1.0, 1.0, 1.0, 0.0);
}

void process() {
	CUSTOM.y += DELTA / max(LIFETIME, 0.001);
	float age = CUSTOM.y;
	float ph = CUSTOM.x * 50.0;
	float t = TIME;
	vec3 v;
	if (kind == 0) {
		v = vec3(sin(t * 0.21 + ph), sin(t * 0.17 + ph * 1.3) * 0.6, cos(t * 0.19 + ph)) * 0.06;
	} else if (kind == 1) {
		v = vec3(sin(t * 0.4 + ph) * 0.12, 0.08 + 0.05 * sin(t + ph), cos(t * 0.35 + ph) * 0.12);
	} else if (kind == 2) {
		v = vec3(sin(t * 1.7 + ph) * 0.5, 1.1 + CUSTOM.z * 1.4, cos(t * 1.3 + ph) * 0.5);
	} else if (kind == 3) {
		v = vec3(sin(t * 1.4 + ph) * 0.6 + 0.25, -0.55 - CUSTOM.z * 0.35, cos(t * 1.1 + ph) * 0.4);
	} else {
		v = vec3(sin(t * 0.7 + ph), sin(t * 1.3 + ph * 2.0) * 0.4, cos(t * 0.6 + ph * 1.7)) * 0.45;
	}
	VELOCITY = v;
	vec3 c = EMISSION_TRANSFORM[3].xyz;
	vec3 p = TRANSFORM[3].xyz;
	vec3 d = p - c;
	if (abs(d.x) > radius) p.x -= sign(d.x) * 2.0 * radius;
	if (abs(d.z) > radius) p.z -= sign(d.z) * 2.0 * radius;
	if (kind != 3 && abs(d.y) > half_height) p.y -= sign(d.y) * 2.0 * half_height;
	if (kind == 3 && (d.y < -half_height || p.y < sky_top(p.xz) - 0.3)) ACTIVE = false; // down on the ground
	TRANSFORM[3].xyz = p;
	float fade = smoothstep(0.0, 0.15, age) * (1.0 - smoothstep(0.8, 1.0, age));
	if (kind == 4) fade *= smoothstep(0.35, 0.9, sin(t * (1.5 + CUSTOM.w * 2.0) + ph)); // blinking
	if (kind == 1) fade *= 0.6 + 0.4 * sin(t * 2.3 + ph);
	if (kind == 2) {
		float sz = mix(1.0, 0.3, age);
		TRANSFORM[0].xyz = vec3(sz, 0.0, 0.0);
		TRANSFORM[1].xyz = vec3(0.0, sz, 0.0);
	}
	if (kind == 3) CUSTOM.w += DELTA * (1.5 + CUSTOM.z * 2.0); // the leaf's spin
	COLOR.a = fade;
}
