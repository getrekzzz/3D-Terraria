// Triplanar sampling of the terrain texture arrays: per-layer detail grain, height-based blending between the three
// surfaces of a triangle, and large-scale colour variation so tiling never shows.
// Albedo alpha is height for opaque layers (opacity for cut-out and translucent ones).
uniform sampler2DArray albedo_array : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray material_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray detail_array : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D surface_table : filter_nearest;
// Per texture layer: x detail layer, y detail strength.
uniform sampler2D layer_table : filter_nearest;
uniform float natural_tile_meters = 2.0;
uniform float crisp_tile_meters = 0.5;
uniform float detail_tile_meters = 0.3;
uniform float normal_strength = 1.0;
// Height band (texture height units) over which neighbouring surfaces blend.
uniform float blend_depth = 0.22;

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

// One projection of one layer: albedo (a = height), material and the tangent-space normal with its detail grain.
void strata_layer(float layer, vec2 uv, float detail_ratio, out vec4 a, out vec4 m, out vec2 t) {
	a = texture(albedo_array, vec3(uv, layer));
	m = texture(material_array, vec3(uv, layer));
	t = m.xy * 2.0 - 1.0;
	vec4 info = texelFetch(layer_table, ivec2(int(layer + 0.5), 0), 0);
	if (info.y > 0.001) {
		vec4 d = texture(detail_array, vec3(uv * detail_ratio, info.x));
		t += (d.xy * 2.0 - 1.0) * info.y;
		a.rgb *= 1.0 - (0.5 - d.z) * info.y * 0.3;
	}
}

// Samples a surface id triplanarly: albedo (a = height), material and a world-space normal perturbation.
void strata_surface(float id, vec3 p, vec3 n, vec3 w, float tile, out vec4 albedo, out vec4 mat, out vec3 bent) {
	vec4 layers = texelFetch(surface_table, ivec2(int(id + 0.5), 0), 0);
	float y_layer = n.y >= 0.0 ? layers.x : layers.z;
	float ratio = tile / detail_tile_meters;
	albedo = vec4(0.0);
	mat = vec4(0.0);
	bent = vec3(0.0);
	vec4 a;
	vec4 m;
	vec2 t;
	// Side projections map texture v to -y, so the tangent y component points down in the world.
	if (w.x > 0.001) {
		strata_layer(layers.y, vec2(p.z * sign(n.x), -p.y) / tile, ratio, a, m, t);
		albedo += w.x * a; mat += w.x * m; bent += w.x * vec3(0.0, -t.y, t.x * sign(n.x));
	}
	if (w.y > 0.001) {
		strata_layer(y_layer, vec2(p.x, p.z * sign(n.y)) / tile, ratio, a, m, t);
		albedo += w.y * a; mat += w.y * m; bent += w.y * vec3(t.x, 0.0, t.y * sign(n.y));
	}
	if (w.z > 0.001) {
		strata_layer(layers.y, vec2(-p.x * sign(n.z), -p.y) / tile, ratio, a, m, t);
		albedo += w.z * a; mat += w.z * m; bent += w.z * vec3(-t.x * sign(n.z), -t.y, 0.0);
	}
}

// Kept for single-surface shaders (cut-out, glass): accumulates one weighted surface.
void strata_sample_surface(float id, vec3 p, vec3 n, vec3 w, float tile, float weight, inout vec4 albedo, inout vec4 mat, inout vec3 bent) {
	if (weight < 0.002) return;
	vec4 a;
	vec4 m;
	vec3 b;
	strata_surface(id, p, n, w, tile, a, m, b);
	albedo += weight * a;
	mat += weight * m;
	bent += weight * b;
}

// Large-scale brightness and hue drift over tens of metres (from the shared sky noise) to break up repetition.
// Requires clouds.gdshaderinc (strata_cloud_noise) to be included first.
vec3 strata_macro(vec3 albedo, vec3 p) {
	float broad = texture(strata_cloud_noise, p.xz / 53.0).r;
	float mid = texture(strata_cloud_noise, p.xz / 17.0 + vec2(0.37, 0.61)).g;
	float v = broad * 0.6 + mid * 0.4;
	return albedo * (0.84 + 0.32 * v) * mix(vec3(1.03, 1.0, 0.95), vec3(0.97, 1.0, 1.04), broad);
}
