// Ambient particle sprites, camera-facing: glowing kinds (spores, embers, fireflies) add light and stay visible in the
// dark; dust and leaves are lit by the scene. The material is created twice (glow and lit) by AmbientParticles.
shader_type spatial;
render_mode blend_mix, depth_draw_never, cull_disabled, shadows_disabled, skip_vertex_transform, unshaded;

// 0 dust, 1 spores, 2 embers, 3 leaves, 4 fireflies.
uniform int kind = 0;
uniform vec4 tint : source_color = vec4(1.0);
uniform float size = 0.03;
uniform float energy = 1.0;
// The light where the camera is (block light plus sky), sampled from the voxel light a few times a second.
uniform vec3 local_light = vec3(1.0);

varying float v_spin;

void vertex() {
	vec3 center = MODEL_MATRIX[3].xyz;
	float s = length(MODEL_MATRIX[0].xyz) * size;
	v_spin = INSTANCE_CUSTOM.w;
	vec2 q = VERTEX.xy;
	if (kind == 3) {
		float a = v_spin;
		q = mat2(vec2(cos(a), sin(a)), vec2(-sin(a), cos(a))) * q * vec2(1.0, 0.55 + 0.45 * abs(sin(a * 0.7)));
	}
	vec3 world = center + (INV_VIEW_MATRIX[0].xyz * q.x + INV_VIEW_MATRIX[1].xyz * q.y) * s;
	VERTEX = (VIEW_MATRIX * vec4(world, 1.0)).xyz;
}

void fragment() {
	vec2 q = UV * 2.0 - 1.0;
	float r2 = dot(q, q);
	float shape;
	if (kind == 3) {
		shape = smoothstep(1.0, 0.8, r2) * (0.75 + 0.25 * q.x);
	} else {
		shape = exp(-r2 * 3.5);
	}
	float a = shape * COLOR.a * tint.a;
	if (kind == 0 || kind == 3) {
		// Lit like the air around the camera: in a dark cave motes only show near a torch or a shaft of daylight.
		ALBEDO = tint.rgb * local_light;
		ALPHA = a * clamp(dot(local_light, vec3(0.3, 0.5, 0.2)) * 3.0, 0.0, 1.0);
	} else {
		ALBEDO = tint.rgb * energy;
		ALPHA = clamp(a, 0.0, 1.0);
	}
}
