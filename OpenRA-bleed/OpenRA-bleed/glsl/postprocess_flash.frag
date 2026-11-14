#version {VERSION}
#ifdef GL_ES
// Mobile GPUs honour mediump (16 bit floats), which is too imprecise to address
// every texel of large sprite sheets, so request full precision.
precision highp float;
#endif

uniform float Blend;
uniform vec3 Color;
uniform sampler2D SourceTexture;
out vec4 fragColor;

void main()
{
	vec4 c = texelFetch(SourceTexture, ivec2(gl_FragCoord.xy), 0);
	fragColor = mix(c, vec4(Color, c.a), Blend);
}
