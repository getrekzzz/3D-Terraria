// Shared voxel lighting for every lit surface (terrain, props, creatures).
// Baked per-vertex/instance light: rgb = block light (torches, lava, crystals), a = sky exposure (0..1).
// Sky light itself (ambient irradiance and reflections from the physical sky) is Godot's environment lighting,
// gated per surface through AO by the baked sky exposure, so caves and sealed rooms stay dark.
global uniform vec4 strata_sky_ambient;
global uniform vec4 strata_block_light_color;
global uniform float strata_daylight;
// Toward the dominant sky light (the sun by day, the moon by night): cloud shadows.
global uniform vec3 strata_light_dir;

// Perceptual response of a 0..1 light level (keeps caves dark while torch halos read clearly).
float strata_light_curve(float level) {
	return level * level * (0.35 + 0.65 * level);
}

// How much of the sky a surface sees (0..1); written to AO so the environment's sky ambient and reflections fade out
// underground.
float strata_sky_visibility(vec4 light) {
	return strata_light_curve(light.a);
}

// Block light (torches, lava, glowing ores) applied through EMISSION. Out in daylight the eye is adapted to the
// sun, so a torch or a lava pool barely tints a sunlit wall; at night and underground it carries the scene.
vec3 strata_block_light(vec3 albedo, vec4 light, float ao) {
	vec3 block = vec3(strata_light_curve(light.r), strata_light_curve(light.g), strata_light_curve(light.b));
	float daylit = strata_light_curve(light.a) * strata_daylight;
	return albedo * block * strata_block_light_color.rgb * 1.6 * ao * (1.0 - 0.65 * daylit);
}

// Sky ambient + block light in one term, for shaders that disable the environment's ambient light (sprites).
vec3 strata_ambient(vec3 albedo, vec4 light, float ao) {
	return albedo * strata_sky_ambient.rgb * strata_light_curve(light.a) * ao + strata_block_light(albedo, light, ao);
}

// Sunlight gate: direct sun/moon only reaches cells open to the sky (caves stay dark beyond shadow range).
float strata_sun_gate(vec4 light) {
	return smoothstep(0.35, 0.95, light.a);
}
