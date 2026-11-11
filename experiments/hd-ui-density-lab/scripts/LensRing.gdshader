// ARC-RES-122 vector lens: a brass ring with bevels around a glass lens, drawn from its distance field in device pixels.
// Every size uniform is in device pixels; the anti-aliased edge is one device pixel wide at any scale.
shader_type canvas_item;

uniform vec2 box_px = vec2(64.0, 64.0);
uniform float outer_r = 30.0;
uniform float ring_w = 6.0;
uniform float bevel_w = 2.0;
uniform vec2 light_dir = vec2(-0.7071, -0.7071);
uniform vec4 brass_light : source_color = vec4(0.84, 0.70, 0.38, 1.0);
uniform vec4 brass_mid : source_color = vec4(0.63, 0.47, 0.20, 1.0);
uniform vec4 brass_dark : source_color = vec4(0.38, 0.27, 0.12, 1.0);
uniform vec4 glass_inner : source_color = vec4(0.55, 0.75, 0.85, 1.0);
uniform vec4 glass_outer : source_color = vec4(0.10, 0.22, 0.32, 1.0);

void fragment() {
	vec2 p = (UV - 0.5) * box_px;
	float d = length(p);
	float inner_r = max(outer_r - ring_w, 0.0);
	float coverage = 1.0 - smoothstep(outer_r - 0.5, outer_r + 0.5, d);
	float ring_mask = smoothstep(inner_r - 0.5, inner_r + 0.5, d);
	vec2 n = d > 0.0 ? p / d : vec2(0.0);
	float lit = clamp(dot(n, light_dir) * 0.5 + 0.5, 0.0, 1.0);
	float bevel = max(bevel_w, 1.0);
	float outer_band = clamp((outer_r - d) / bevel, 0.0, 1.0);
	float inner_band = clamp((d - inner_r) / bevel, 0.0, 1.0);
	vec3 ring = brass_mid.rgb;
	ring = mix(mix(brass_dark.rgb, brass_light.rgb, lit), ring, outer_band);
	ring = mix(mix(brass_light.rgb, brass_dark.rgb, lit), ring, inner_band);
	float r01 = clamp(d / max(inner_r, 1.0), 0.0, 1.0);
	vec3 glass = mix(glass_inner.rgb, glass_outer.rgb, r01 * r01);
	float spot_r = inner_r * 0.14;
	float spot = 1.0 - smoothstep(spot_r - 0.5, spot_r + 0.5, length(p - light_dir * inner_r * 0.45));
	glass = mix(glass, vec3(1.0), spot * 0.65);
	COLOR = vec4(mix(glass, ring, ring_mask), coverage);
}
