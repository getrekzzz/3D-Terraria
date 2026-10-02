// Rain-soaked surfaces (strata_weather.x wetness, .y puddles; set by WeatherRenderer). Only what the sky reaches gets
// wet (baked sky light), so caves and the inside of houses stay dry: wet albedo darkens and its roughness drops into a
// sheen, and on flat tops water gathers into glossy puddles that mirror the sky.
#include "res://shaders/include/noise.gdshaderinc"

global uniform vec4 strata_weather;

void strata_wet(vec3 n, vec3 world_pos, float sky_light, bool puddles, inout vec3 albedo, inout float roughness, inout vec3 world_n) {
	float exposed = smoothstep(0.62, 0.95, sky_light);
	float wet = strata_weather.x * exposed;
	if (wet <= 0.002) return;
	float up = smoothstep(0.1, 0.85, n.y);
	wet *= mix(0.5, 1.0, up); // walls shed the water, tops hold it
	albedo *= mix(1.0, 0.6, wet);
	roughness = mix(roughness, 0.2, wet * 0.85);
	if (!puddles) return;
	float flat_top = smoothstep(0.93, 0.99, n.y);
	float pool = strata_value_noise(world_pos.xz * 0.42) * 0.7 + strata_value_noise(world_pos.xz * 1.7 + 9.1) * 0.3;
	float puddle = flat_top * exposed * smoothstep(0.62, 0.7, pool + strata_weather.y * 0.3);
	puddle *= strata_weather.y;
	albedo *= mix(1.0, 0.68, puddle);
	roughness = mix(roughness, 0.03, puddle);
	world_n = normalize(mix(world_n, vec3(0.0, 1.0, 0.0), puddle));
}
