#version {VERSION}
#ifdef GL_ES
// Mobile GPUs honour mediump (16 bit floats), which is too imprecise to address
// every texel of large sprite sheets, so request full precision.
precision highp float;
#endif

uniform vec3 Tint;
uniform sampler2D WorldTexture;
out vec4 fragColor;

void main()
{
	vec4 c = texelFetch(WorldTexture, ivec2(gl_FragCoord.xy), 0);
	fragColor = vec4(Tint, c.a) * c;
}
