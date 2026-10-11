// The far view (game/World/HorizonRenderer.cs): a heightfield of the world's surface beyond the streamed chunks,
// coloured by the surface biome, with the sea over low ground. Vertex COLOR: rgb biome colour (sRGB), a = 1 on water.
// Where real chunks are loaded (the streamed box) it is discarded, so it only fills the distance; fog and aerial
// perspective blend it into the sky.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#include "res://shaders/include/voxel_light.gdshaderinc"
#include "res://shaders/include/voxel_brdf.gdshaderinc"
#include "res://shaders/include/clouds.gdshaderinc"

// xz min and max of the loaded chunk box (metres), and its vertical range in y (metres).
uniform vec4 streamed_box = vec4(0.0);
uniform vec2 streamed_height = vec2(-1e6, 1e6);

varying vec3 v_world;
varying float f_sun_gate;

void vertex() {
	v_world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 p = v_world.xz;
	bool inside = p.x > streamed_box.x && p.y > streamed_box.y && p.x < streamed_box.z && p.y < streamed_box.w
		&& v_world.y > streamed_height.x && v_world.y < streamed_height.y;
	if (inside) {
		discard;
	}
	bool water = COLOR.a > 0.5;
	vec3 base = pow(COLOR.rgb, vec3(2.2));
	// Macro variation (woods, clearings, rock) so the distance is not flat paint.
	float n = texture(strata_cloud_noise, p / 90.0).g;
	float fine = texture(strata_cloud_noise, p / 17.0 + 0.3).b;
	if (water) {
		ALBEDO = base * 0.6;
		ROUGHNESS = 0.08 + fine * 0.05;
		SPECULAR = 0.6;
		AO = 1.0;
	} else {
		float slope = 1.0 - clamp(dot(normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz), vec3(0.0, 1.0, 0.0)), 0.0, 1.0);
		vec3 rock = vec3(0.34, 0.32, 0.3);
		// Green land is mostly woodland from afar: dark canopy clumps with lighter clearings between them.
		float green = clamp((base.g - max(base.r, base.b)) * 6.0, 0.0, 1.0);
		float clumps = texture(strata_cloud_noise, p / 34.0 + 0.61).g * 0.7 + texture(strata_cloud_noise, p / 9.0 + 0.2).b * 0.3;
		float canopy = smoothstep(0.38, 0.62, clumps) * green;
		base = mix(base, base * vec3(0.42, 0.5, 0.4), canopy);
		base = mix(base * (0.8 + 0.4 * n) * (0.88 + 0.24 * fine), rock, smoothstep(0.35, 0.7, slope));
		// Bright sand and snow map colours would outshine the real (textured, shadowed) terrain in front of them.
		float l = dot(base, vec3(0.2126, 0.7152, 0.0722));
		base *= min(1.0, 0.42 / max(l, 1e-3));
		ALBEDO = base;
		ROUGHNESS = 0.9;
		SPECULAR = 0.3;
		// Unresolved small-scale shading (dunes, trees, rocks) the coarse heightfield lacks.
		AO = 0.8;
	}
	f_sun_gate = strata_cloud_shadow(v_world, strata_light_dir);
}

void light() {
	strata_direct_light(NORMAL, LIGHT, VIEW, LIGHT_COLOR, ATTENUATION, LIGHT_IS_DIRECTIONAL, f_sun_gate, ROUGHNESS, 0.0, ALBEDO, DIFFUSE_LIGHT, SPECULAR_LIGHT);
}
