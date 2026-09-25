// Shared voxel lighting for every lit surface (terrain, props, creatures).
// Baked per-vertex/instance light: rgb = block light (torches, lava, crystals), a = sky exposure (0..1).
global uniform vec4 strata_sky_ambient;
global uniform vec4 strata_block_light_color;
global uniform float strata_daylight;

// Perceptual response of a 0..1 light level (keeps caves dark while torch halos read clearly).
float strata_light_curve(float level) {
	return level * level * (0.35 + 0.65 * level);
}

// Ambient + block light contribution, applied through EMISSION (environment ambient is black).
vec3 strata_ambient(vec3 albedo, vec4 light, float ao) {
	float sky = strata_light_curve(light.a);
	vec3 block = vec3(strata_light_curve(light.r), strata_light_curve(light.g), strata_light_curve(light.b));
	return albedo * (strata_sky_ambient.rgb * sky + block * strata_block_light_color.rgb * 1.6) * ao;
}

// Sunlight gate: direct sun/moon only reaches cells open to the sky (caves stay dark beyond shadow range).
float strata_sun_gate(vec4 light) {
	return smoothstep(0.35, 0.95, light.a);
}
