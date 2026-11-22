#version {VERSION}
#ifdef GL_ES
// Mobile GPUs honour mediump (16 bit floats), which is too imprecise to address
// every texel of large sprite sheets, so request full precision.
precision highp float;
#endif

uniform float Blend;
uniform sampler2D WorldTexture;
out vec4 fragColor;

void main()
{
	vec4 c = texelFetch(WorldTexture, ivec2(gl_FragCoord.xy), 0);
	float lum = 0.5 * (min(c.r, min(c.g, c.b)) + max(c.r, max(c.g, c.b)));
	fragColor = mix(c, vec4(lum, lum, lum, c.a), Blend);
}
