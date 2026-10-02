// Gore chunks (GoreSystem): a MultiMesh of small boxes and balls in each chunk's colour (instance colour, alpha fades
// them out), lit by the voxel light sampled where the chunk lies (instance custom: block rgb, sky a).
shader_type spatial;
render_mode blend_mix, cull_back, unshaded, depth_draw_opaque;

#include "res://shaders/include/voxel_light.gdshaderinc"

varying vec4 v_light;

void vertex() {
	v_light = INSTANCE_CUSTOM;
}

void fragment() {
	vec3 block = strata_block_light(vec3(1.0), v_light, 1.0);
	// Neutral daylight (the sky ambient's brightness, not its blue) so colours stay true outdoors.
	vec3 sky = vec3(dot(strata_sky_ambient.rgb, vec3(0.3, 0.5, 0.2))) * strata_sky_visibility(v_light) * 1.5;
	// A little shading by facing so the chunks read as solid.
	float facing = 0.75 + 0.25 * clamp(dot(NORMAL, vec3(0.0, 1.0, 0.0)) * 0.5 + 0.5, 0.0, 1.0);
	ALBEDO = COLOR.rgb * clamp(block + sky + vec3(0.02), vec3(0.0), vec3(1.15)) * facing;
	ALPHA = COLOR.a;
}
