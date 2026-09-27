// The cloud layer shared by the sky (drawing it) and lit surfaces (its shadows): one noise field at a fixed altitude,
// scrolled by wind, so the shadows sweeping over the land match the clouds overhead.
// strata_cloud_noise: tileable RGBA noise (r broad billows, g medium detail, b fine detail, a unused).
// strata_clouds: xy wind offset (m), z coverage (0 clear .. 1 overcast), w altitude (m).
global uniform sampler2D strata_cloud_noise : filter_linear_mipmap, repeat_enable;
global uniform vec4 strata_clouds;

const float STRATA_CLOUD_TILE = 7000.0;

// Cloud density (0..1) at a world position on the cloud layer (metres, xz).
float strata_cloud_density(vec2 xz, float lod_detail) {
	vec2 uv = (xz + strata_clouds.xy) / STRATA_CLOUD_TILE;
	float broad = texture(strata_cloud_noise, uv).r;
	float medium = texture(strata_cloud_noise, uv * 3.7 + vec2(0.31, 0.77)).g;
	float fine = texture(strata_cloud_noise, uv * 13.1 + vec2(0.53, 0.19)).b;
	float n = broad * 0.62 + medium * 0.28 + fine * 0.10 * lod_detail;
	float cover = clamp(strata_clouds.z, 0.0, 1.0);
	float lo = mix(0.78, 0.18, cover);
	return smoothstep(lo, lo + 0.28, n);
}

// Fraction of direct light (0..1) left after the cloud layer, for a surface at `world` lit from `to_light`.
float strata_cloud_shadow(vec3 world, vec3 to_light) {
	if (to_light.y < 0.02 || strata_clouds.z <= 0.001) return 1.0;
	float rise = strata_clouds.w - world.y;
	if (rise <= 0.0) return 1.0;
	vec2 xz = world.xz + to_light.xz / to_light.y * rise;
	float d = strata_cloud_density(xz, 0.0);
	return 1.0 - d * 0.72;
}
