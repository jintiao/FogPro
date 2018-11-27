sampler2D _CameraDepthTexture;
sampler3D _VolumeScatter;
float _CameraFarOverMaxFar;
float _NearOverFarClip;

half4 Fog(half2 screenuv)
{
	half z = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, screenuv)) * _CameraFarOverMaxFar;
	z = (z - _NearOverFarClip) / (1 - _NearOverFarClip);
	if (z < 0.0)
		return half4(0, 0, 0, 1);

	half3 uvw = half3(screenuv.x, screenuv.y, z);
	return tex3D(_VolumeScatter, uvw);
}
