// Red screen edges when the player is hurt: a radial vignette whose strength (alpha) the HurtOverlay fades out.
shader_type canvas_item;

uniform float strength = 0.0;
uniform vec4 tint : source_color = vec4(0.75, 0.02, 0.02, 1.0);

void fragment() {
	vec2 d = (UV - 0.5) * vec2(1.25, 1.0);
	float edge = smoothstep(0.36, 0.8, length(d));
	COLOR = vec4(tint.rgb, tint.a * strength * edge);
}
