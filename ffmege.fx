struct VS_IN
{
	float4 pos : POSITION;
	float2 tex : TEXCOORD;
};

struct PS_IN
{
	float4 pos : SV_POSITION;
	float2 tex : TEXCOORD;
};

//Texture2D<float> picture : register(t0);
Texture2D<float> tex2d1 : register(t0);
Texture2D<float2> tex2d2 : register(t1);
SamplerState pictureSampler : register(s0);

PS_IN VS(VS_IN input)
{
	PS_IN output = (PS_IN)0;

	output.pos = input.pos;
	output.tex = input.tex;

	return output;
}

// Derived from https://msdn.microsoft.com/en-us/library/windows/desktop/dd206750(v=vs.85).aspx
// Section: Converting 8-bit YUV to RGB888
static const float3x3 YUVtoRGBCoeffMatrix =
{
	1.164383f,  1.164383f, 1.164383f,
	0.000000f, -0.391762f, 2.017232f,
	1.596027f, -0.812968f, 0.000000f
};

float3 ConvertYUVtoRGB(float3 yuv)
{
	// Derived from https://msdn.microsoft.com/en-us/library/windows/desktop/dd206750(v=vs.85).aspx
	// Section: Converting 8-bit YUV to RGB888

	// These values are calculated from (16 / 255) and (128 / 255)
	yuv -= float3(0.062745f, 0.501960f, 0.501960f);
	yuv = mul(yuv, YUVtoRGBCoeffMatrix);

	return saturate(yuv);
}

float4 PS(PS_IN input) : SV_Target
{	
	float y = tex2d1.Sample(pictureSampler, input.tex);
	float2 uv = tex2d2.Sample(pictureSampler, input.tex);

	return min16float4(ConvertYUVtoRGB(float3(y, uv)), 1.f);
}