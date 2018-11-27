using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System;
using System.Runtime.InteropServices;

namespace FogPro
{
	struct PointLightParams
	{
		public Vector3 pos;
		public float range;
		public Vector3 color;
		float padding;
	}

	public class FPFogPro : MonoBehaviour
	{
		private const int injectNumThreadX = 16;
		private const int injectNumThreadY = 2;
		private const int injectNumThreadZ = 16;
		private const int scatterNumThreadX = 32;
		private const int scatterNumThreadY = 2;
		private const int volumeResolutionX = 160;
		private const int volumeResolutionY = 90;
		private const int volumeResolutionZ = 128;

		public RenderTexture texInject;
		public RenderTexture texScatter;

		public ComputeShader csInject;
		public ComputeShader csScatter;

		public List<Light> lights = new List<Light>();

		private PointLightParams[] pointLightParams;
		private ComputeBuffer pointLightParamBuffer;

		[Range(0, 5)]
		public float globalIntensity = 1.0f;

		[Range(0, 5)]
		public float ambientLightIntensity = 0.0f;

		public Color ambientLightColor = Color.white;

		[Range(0.1f, 1000)]
		public float nearClip = 0.1f;

		[Range(0.1f, 1000)]
		public float farClip = 100.0f;

		// Density
		[Range(0, 5)]
		public float constantFog = 0;
		[Range(0, 5)]
		public float heightFogAmount = 0;
		[Range(0, 5)]
		public float heightFogExponent = 0;
		[Range(0, 5)]
		public float heightFogOffset = 0;

		[Range(0, 5)]
		public float globalDensity = 1.0f;

		public Material matApplyFog;

		private Camera targetCamera;

		[ImageEffectOpaque]
		void OnRenderImage(RenderTexture src, RenderTexture dest)
		{
			if (!CheckSupport())
			{
				Debug.LogError("unsupport platform detected, disabling ForPro...");
				enabled = false;

				Graphics.Blit(src, dest);
				return;
			}

			CalcLightScatter();
			ApplyFog(src, dest);
		}

		private bool CheckSupport()
		{
			//if (!SystemInfo.supportsComputeShaders)
			//	return false;

			//if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
			//	return false;

			return true;
		}

		private void CalcLightScatter()
		{
			Setup();
			DoInject();
			DoScatter();
		}

		private void Setup()
		{
			if (texInject == null)
				texInject = CreateVolumeTexture();

			if (texScatter == null)
				texScatter = CreateVolumeTexture();

			if (targetCamera == null)
				targetCamera = GetComponent<Camera>();

			SetupLights();
		}

		private RenderTexture CreateVolumeTexture()
		{
			var tex = new RenderTexture(volumeResolutionX, volumeResolutionY, 0, RenderTextureFormat.ARGBHalf);
			tex.dimension = TextureDimension.Tex3D;
			tex.volumeDepth = volumeResolutionZ;
			tex.enableRandomWrite = true;
			tex.Create();
			return tex;
		}

		private void SetupLights()
		{
			int pointLightCount = 0;

			foreach(var light in lights)
			{
				switch (light.type)
				{
				case LightType.Point:
					pointLightCount++;
					break;
				}
			}

			if (pointLightParams == null || pointLightParams.Length != pointLightCount)
			{
				pointLightParams = new PointLightParams[pointLightCount];

				if (pointLightParamBuffer != null)
					pointLightParamBuffer.Release();
				pointLightParamBuffer = new ComputeBuffer(pointLightCount, Marshal.SizeOf(typeof(PointLightParams)));
			}
		}

		static readonly Vector2[] frustumUVs = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
		float[] frustumRays = new float[16];
		float[] fogParams = new float[4];
		private float[] ambientLight = new float[3];

		private void DoInject()
		{
			var kernel = csInject.FindKernel("InjectMain");

			var cam = targetCamera.transform.position;
			csInject.SetVector("_CameraPos", cam);

			csInject.SetFloat("_NearOverFarClip", nearClip / farClip);

			for (int i = 0; i < 4; i++)
			{
				Vector3 ray = targetCamera.ViewportToWorldPoint(new Vector3(frustumUVs[i].x, frustumUVs[i].y, farClip)) - cam;
				frustumRays[i * 4 + 0] = ray.x;
				frustumRays[i * 4 + 1] = ray.y;
				frustumRays[i * 4 + 2] = ray.z;
				frustumRays[i * 4 + 3] = 0;
			}
			csInject.SetFloats("_FrustumRays", frustumRays);

			fogParams[0] = constantFog;
			fogParams[1] = heightFogExponent;
			fogParams[2] = heightFogOffset;
			fogParams[3] = heightFogAmount;
			csInject.SetFloats("_FogParams", fogParams); 
			csInject.SetFloat("_Density", globalDensity);

			Color ambient = ambientLightColor * ambientLightIntensity * 0.1f;
			ambientLight[0] = ambient.r;
			ambientLight[1] = ambient.g;
			ambientLight[2] = ambient.b;
			csInject.SetFloats("_AmbientLight", ambientLight);

			csInject.SetFloat("_Intensity", globalIntensity);

			int index = 0;
			foreach (var light in lights)
			{
				if (light.type != LightType.Point)
					continue;

				pointLightParams[index].pos = light.transform.position;
				pointLightParams[index].range = 1.0f / (light.range * light.range);
				pointLightParams[index].color = new Vector3(light.color.r, light.color.g, light.color.b);
				index++;
			}
			pointLightParamBuffer.SetData(pointLightParams);
			csInject.SetFloat("_PointLightsCount", pointLightParams.Length);
			csInject.SetBuffer(kernel, "_PointLights", pointLightParamBuffer);

			csInject.SetTexture(kernel, "_VolumeInject", texInject);
			csInject.Dispatch(kernel, volumeResolutionX / injectNumThreadX, volumeResolutionY / injectNumThreadY, volumeResolutionZ / injectNumThreadZ);
		}

		private void DoScatter()
		{
			var kernel = csScatter.FindKernel("ScatterMain");
			csScatter.SetTexture(kernel, "_VolumeInject", texInject);
			csScatter.SetTexture(kernel, "_VolumeScatter", texScatter);
			csScatter.Dispatch(kernel, volumeResolutionX / scatterNumThreadX, volumeResolutionY / scatterNumThreadY, 1);
		}

		private void ApplyFog(RenderTexture src, RenderTexture dest)
		{
			if (matApplyFog == null)
				matApplyFog = new Material(Shader.Find("Hidden/FogPro/ApplyFog"));

			Shader.SetGlobalFloat("_CameraFarOverMaxFar", targetCamera.farClipPlane / farClip);
			Shader.SetGlobalFloat("_NearOverFarClip", nearClip / farClip);

			matApplyFog.SetTexture("_VolumeScatter", texScatter);
			Graphics.Blit(src, dest, matApplyFog);
		}
	}
}
