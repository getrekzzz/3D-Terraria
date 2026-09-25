// Lambert + normalized Blinn-Phong used by custom light() functions.
// Requires a float varying `f_sun_gate` written in fragment().
void strata_direct_light(vec3 normal, vec3 light_dir, vec3 view, vec3 light_color, float attenuation,
		bool directional, float gate, float roughness, float metallic, vec3 albedo,
		inout vec3 diffuse_out, inout vec3 specular_out) {
	float g = directional ? gate : 1.0;
	float ndl = clamp(dot(normal, light_dir), 0.0, 1.0);
	diffuse_out += ndl * attenuation * light_color / PI * g;
	vec3 h = normalize(view + light_dir);
	float power = mix(4.0, 256.0, pow(1.0 - roughness, 2.0));
	float spec = pow(clamp(dot(normal, h), 0.0, 1.0), power) * (power + 8.0) / (8.0 * PI);
	vec3 f0 = mix(vec3(0.04), albedo, metallic);
	specular_out += spec * ndl * attenuation * light_color * f0 * g;
}
