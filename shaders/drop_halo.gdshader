// Additive, camera-facing soft halo behind a dropped item (P3-36): rarity colour or a pickup's glow, set per instance.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;

instance uniform vec4 halo_color = vec4(1.0, 1.0, 1.0, 0.5);

void vertex() {
	float scale = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * scale, INV_VIEW_MATRIX[1] * scale, INV_VIEW_MATRIX[2] * scale, MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
}

void fragment() {
	float d = length(UV - vec2(0.5)) * 2.0;
	float falloff = pow(clamp(1.0 - d, 0.0, 1.0), 2.2);
	ALBEDO = halo_color.rgb * falloff * halo_color.a;
	ALPHA = 1.0;
}
