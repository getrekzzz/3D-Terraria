// Translucent props (prop:<layer>:clear slots): slime gel, crystal wings. The prop shader with alpha, thicker at the
// rim; drawn after opaque geometry so what is inside (a slime's crown, a swallowed ninja) shows through.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

#define PROP_CLEAR
#include "res://shaders/include/prop_common.gdshaderinc"
