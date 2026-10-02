// Crack decals on damaged blocks: a box drawn by its back faces that reconstructs the visible surface from the depth
// buffer, keeps only what lies inside the box and stamps a triplanar crack stage on it. Works on the smooth
// surface-nets terrain as well as crisp blocks. Per instance: custom.r = stage (0–3) / 3, custom.g = fade (0–1).
shader_type spatial;
render_mode unshaded, cull_front, depth_test_disabled, depth_draw_never, blend_mix, shadows_disabled, fog_disabled;

uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;
uniform sampler2D cracks : source_color, filter_linear_mipmap, repeat_disable;
/// World size the crack tile spans (one building-grid block), so neighbouring cells continue the pattern.
uniform float tile_size = 1.0;

varying flat vec2 stage_fade;
varying flat vec3 box_center;
varying flat vec3 box_half;

void vertex() {
	stage_fade = INSTANCE_CUSTOM.rg;
	box_center = MODEL_MATRIX[3].xyz;
	box_half = vec3(length(MODEL_MATRIX[0].xyz), length(MODEL_MATRIX[1].xyz), length(MODEL_MATRIX[2].xyz)) * 0.5;
}

void fragment() {
	float depth = texture(depth_tex, SCREEN_UV).r;
	vec4 view = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth, 1.0);
	view.xyz /= view.w;
	vec3 world = (INV_VIEW_MATRIX * vec4(view.xyz, 1.0)).xyz;
	vec3 local = (world - box_center) / box_half;
	if (any(greaterThan(abs(local), vec3(1.0)))) discard;
	vec3 n = abs(normalize(cross(dFdx(world), dFdy(world))));
	vec2 uv = n.x > n.y && n.x > n.z ? world.zy : (n.y > n.z ? world.xz : world.xy);
	uv /= tile_size;
	float stage = floor(stage_fade.x * 3.0 + 0.5);
	// Gradients of the continuous coordinates, so the wrap at each tile edge does not pick a tiny mip (seam lines).
	vec2 g = uv * vec2(0.25, 1.0);
	float mask = textureGrad(cracks, vec2((stage + fract(uv.x)) * 0.25, fract(uv.y)), dFdx(g), dFdy(g)).a;
	ALBEDO = vec3(0.04, 0.035, 0.03);
	ALPHA = mask * 0.85 * stage_fade.y;
}
