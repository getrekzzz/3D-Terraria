// EffectKit particles (chips, dust, sparks, puffs, splashes, leaves): the particle colour lit by the voxel light where
// the burst happened (instance uniform voxel_light: block light rgb, sky in a), so chips in a dark cave are dark and
// the same chips in daylight are bright. Sparks glow instead. Quads face the camera; chips stay solid tumbling boxes.
shader_type spatial;
render_mode blend_mix, cull_disabled, shadows_disabled, unshaded, depth_draw_opaque;

#include "res://shaders/include/voxel_light.gdshaderinc"


// 0 solid (chips), 1 soft quad (dust, puffs, splashes, leaves), 2 glowing quad (sparks).
uniform int style = 0;
instance uniform vec4 voxel_light = vec4(0.0, 0.0, 0.0, 1.0);
uniform float glow = 3.0;

void vertex() {
	if (style != 0) {
		// Billboard the quad, keeping the particle's scale.
		float s = length(MODEL_MATRIX[0].xyz);
		MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
	}
}

void fragment() {
	vec3 col = COLOR.rgb;
	float a = COLOR.a;
	if (style != 0) {
		vec2 q = UV * 2.0 - 1.0;
		a *= clamp(1.0 - dot(q, q), 0.0, 1.0);
	}
	if (style == 2) {
		ALBEDO = col * glow;
	} else {
		// Block light through the shared response curve plus the sky's share of daylight.
		vec3 block = strata_block_light(vec3(1.0), voxel_light, 1.0);
		vec3 sky = strata_sky_ambient.rgb * strata_sky_visibility(voxel_light) * 1.6;
		ALBEDO = col * clamp(block + sky + vec3(0.02), vec3(0.0), vec3(1.6));
	}
	ALPHA = a;
}
