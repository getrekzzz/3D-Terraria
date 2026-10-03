// Direct lighting for custom light() functions: GGX specular (height-correlated Smith visibility, Schlick Fresnel)
// with Burley diffuse. Godot multiplies DIFFUSE_LIGHT by albedo and (1 - metallic) afterwards; LIGHT_COLOR already
// carries energy * PI. The sun gate (sky exposure, cloud shadow) applies to directional lights only.
void strata_direct_light(vec3 normal, vec3 light_dir, vec3 view, vec3 light_color, float attenuation,
		bool directional, float gate, float roughness, float metallic, vec3 albedo,
		inout vec3 diffuse_out, inout vec3 specular_out) {
	float g = directional ? gate : 1.0;
	float nol = dot(normal, light_dir);
	if (nol <= 0.0 || g <= 0.0) return;
	vec3 h = normalize(view + light_dir);
	float nov = max(dot(normal, view), 1e-4);
	float noh = clamp(dot(normal, h), 0.0, 1.0);
	float loh = clamp(dot(light_dir, h), 0.0, 1.0);
	float r = clamp(roughness, 0.03, 1.0);
	float a = r * r;
	float a2 = a * a;
	float fd90 = 0.5 + 2.0 * loh * loh * r;
	// Rounding can push a dot product of unit vectors just past 1, and pow() of a negative number is NaN.
	float fl = 1.0 + (fd90 - 1.0) * pow(max(1.0 - nol, 0.0), 5.0);
	float fv = 1.0 + (fd90 - 1.0) * pow(max(1.0 - nov, 0.0), 5.0);
	vec3 radiance = light_color * attenuation * g;
	diffuse_out += radiance * nol * fl * fv / PI;
	float dd = noh * noh * (a2 - 1.0) + 1.0;
	float d = a2 / (PI * dd * dd);
	float vis = 0.5 / max(nol * sqrt(nov * nov * (1.0 - a2) + a2) + nov * sqrt(nol * nol * (1.0 - a2) + a2), 1e-5);
	vec3 f0 = mix(vec3(0.04), albedo, metallic);
	vec3 f = f0 + (1.0 - f0) * pow(1.0 - loh, 5.0);
	specular_out += radiance * nol * d * vis * f;
}

// Light passing through thin foliage (leaves, grass): wrapped diffuse plus a forward-scattering lobe when the light
// is behind the surface.
void strata_foliage_light(vec3 normal, vec3 light_dir, vec3 view, vec3 light_color, float attenuation,
		bool directional, float gate, float translucency, inout vec3 diffuse_out) {
	float g = directional ? gate : 1.0;
	vec3 radiance = light_color * attenuation * g;
	float wrap = clamp((dot(normal, light_dir) + 0.35) / 1.35, 0.0, 1.0);
	float through = pow(clamp(dot(view, -light_dir), 0.0, 1.0), 6.0) * translucency;
	diffuse_out += radiance * (wrap + through * 1.6) / PI;
}
