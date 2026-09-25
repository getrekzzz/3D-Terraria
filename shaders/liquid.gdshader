shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"

// Water (1), lava (2), honey (3). Type arrives in CUSTOM0.x.
global uniform float strata_time;
uniform sampler2D depth_texture : hint_depth_texture, filter_linear_mipmap;
uniform vec3 water_shallow : source_color = vec3(0.18, 0.48, 0.62);
uniform vec3 water_deep : source_color = vec3(0.02, 0.10, 0.22);
uniform vec3 lava_hot : source_color = vec3(1.0, 0.62, 0.12);
uniform vec3 lava_crust : source_color = vec3(0.35, 0.05, 0.01);
uniform vec3 honey_color : source_color = vec3(0.86, 0.55, 0.08);

varying vec3 v_world_pos;
varying flat float v_type;
varying vec4 v_light;
varying float f_sun_gate;

float wave(vec2 p, float t) {
	return sin(p.x * 1.7 + t * 1.3) * 0.5 + sin(p.y * 2.3 - t * 1.1) * 0.35 + sin((p.x + p.y) * 3.1 + t * 2.0) * 0.15;
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
	float dx = wave(p + vec2(0.1, 0.0), t) - wave(p - vec2(0.1, 0.0), t);
	float dz = wave(p + vec2(0.0, 0.1), t) - wave(p - vec2(0.0, 0.1), t);
	vec3 world_n = normalize(vec3(-dx * 0.6, 1.0, -dz * 0.6));
	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
	if (type == 2) {
		float flow = sin(p.x * 0.9 + t * 0.4) * sin(p.y * 1.1 - t * 0.3) * 0.5 + 0.5;
		vec3 c = mix(lava_crust, lava_hot, smoothstep(0.25, 0.75, flow));
		ALBEDO = c * 0.3;
		EMISSION = c * 3.2;
		ALPHA = 1.0;
		ROUGHNESS = 0.8;
	} else if (type == 3) {
		ALBEDO = honey_color;
		ALPHA = 0.88;
		ROUGHNESS = 0.15;
		EMISSION = strata_ambient(honey_color, v_light, 1.0);
	} else {
		float depth = texture(depth_texture, SCREEN_UV).r;
		vec4 behind = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
		behind.xyz /= behind.w;
		float thickness = clamp((-behind.z) - (-VERTEX.z), 0.0, 20.0);
		float murk = 1.0 - exp(-thickness * 0.35);
		vec3 c = mix(water_shallow, water_deep, murk);
		ALBEDO = c;
		ALPHA = clamp(0.35 + murk * 0.6 + fresnel * 0.3, 0.0, 0.96);
		ROUGHNESS = 0.04;
		EMISSION = strata_ambient(c, v_light, 1.0) * 0.8;
		NORMAL = normalize((VIEW_MATRIX * vec4(world_n, 0.0)).xyz);
	}
	f_sun_gate = strata_sun_gate(v_light);
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, 0.0, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
