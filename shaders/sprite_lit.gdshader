// Camera-facing sprite (dropped items, particles) lit by the voxel light sampled at its position.
shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha, ambient_light_disabled;

#include "res://shaders/include/voxel_light.gdshaderinc"

uniform sampler2D icon : source_color, filter_linear_mipmap, repeat_disable;
instance uniform vec4 voxel_light = vec4(0.0, 0.0, 0.0, 1.0);
instance uniform float glow = 0.0;

void vertex() {
	// Y-axis billboard (same construction as the built-in fixed-Y billboard), keeping the instance scale.
	float scale = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(
		vec4(normalize(cross(vec3(0.0, 1.0, 0.0), INV_VIEW_MATRIX[2].xyz)) * scale, 0.0),
		vec4(0.0, scale, 0.0, 0.0),
		vec4(normalize(cross(INV_VIEW_MATRIX[0].xyz, vec3(0.0, 1.0, 0.0))) * scale, 0.0),
		MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	vec4 tex = texture(icon, UV);
	if (tex.a < 0.5) discard;
	ALBEDO = tex.rgb;
	ROUGHNESS = 0.8;
	SPECULAR = 0.2;
	EMISSION = strata_ambient(tex.rgb, voxel_light, 1.0) + tex.rgb * glow;
}

void light() {
	float gate = strata_sun_gate(voxel_light);
	DIFFUSE_LIGHT += ALBEDO * ATTENUATION * LIGHT_COLOR * gate * max(dot(NORMAL, LIGHT), 0.35) / PI;
}
