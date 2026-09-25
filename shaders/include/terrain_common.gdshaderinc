// Triplanar sampling of the terrain texture arrays with 3-surface barycentric blending.
uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray material_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D surface_table : filter_nearest;
uniform float natural_tile_meters = 2.0;
uniform float crisp_tile_meters = 0.5;

varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying flat vec4 v_ids;
varying vec4 v_light;
varying float f_sun_gate;

vec3 strata_tri_weights(vec3 n, bool crisp) {
	vec3 a = abs(n);
	if (crisp) {
		if (a.x >= a.y && a.x >= a.z) return vec3(1.0, 0.0, 0.0);
		if (a.y >= a.z) return vec3(0.0, 1.0, 0.0);
		return vec3(0.0, 0.0, 1.0);
	}
	a = pow(a, vec3(4.0));
	return a / (a.x + a.y + a.z);
}

// Accumulates weighted albedo and material (normal xy, roughness, metallic) for one surface id.
void strata_sample_surface(float id, vec3 p, vec3 n, vec3 w, float tile, float weight, inout vec4 albedo, inout vec4 mat, inout vec3 bent) {
	if (weight < 0.002) return;
	vec4 layers = texelFetch(surface_table, ivec2(int(id + 0.5), 0), 0);
	float y_layer = n.y >= 0.0 ? layers.x : layers.z;
	vec2 uv_x = vec2(p.z * sign(n.x), -p.y) / tile;
	vec2 uv_y = vec2(p.x, p.z * sign(n.y)) / tile;
	vec2 uv_z = vec2(-p.x * sign(n.z), -p.y) / tile;
	vec4 a = vec4(0.0);
	vec4 m = vec4(0.0);
	if (w.x > 0.001) { a += w.x * texture(albedo_array, vec3(uv_x, layers.y)); vec4 mx = texture(material_array, vec3(uv_x, layers.y)); m += w.x * mx; bent += weight * w.x * vec3(0.0, mx.y * 2.0 - 1.0, (mx.x * 2.0 - 1.0) * sign(n.x)); }
	if (w.y > 0.001) { a += w.y * texture(albedo_array, vec3(uv_y, y_layer)); vec4 my = texture(material_array, vec3(uv_y, y_layer)); m += w.y * my; bent += weight * w.y * vec3(my.x * 2.0 - 1.0, 0.0, (my.y * 2.0 - 1.0) * sign(n.y)); }
	if (w.z > 0.001) { a += w.z * texture(albedo_array, vec3(uv_z, layers.y)); vec4 mz = texture(material_array, vec3(uv_z, layers.y)); m += w.z * mz; bent += weight * w.z * vec3(-(mz.x * 2.0 - 1.0) * sign(n.z), mz.y * 2.0 - 1.0, 0.0); }
	albedo += weight * a;
	mat += weight * m;
}
