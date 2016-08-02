//  Copyright(c) 2016, Michal Skalsky
//  All rights reserved.
//
//  Redistribution and use in source and binary forms, with or without modification,
//  are permitted provided that the following conditions are met:
//
//  1. Redistributions of source code must retain the above copyright notice,
//     this list of conditions and the following disclaimer.
//
//  2. Redistributions in binary form must reproduce the above copyright notice,
//     this list of conditions and the following disclaimer in the documentation
//     and/or other materials provided with the distribution.
//
//  3. Neither the name of the copyright holder nor the names of its contributors
//     may be used to endorse or promote products derived from this software without
//     specific prior written permission.
//
//  THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY
//  EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
//  OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.IN NO EVENT
//  SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
//  SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT
//  OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION)
//  HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR
//  TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE,
//  EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.



using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;
using System;
using System.Text;

[RequireComponent(typeof(Camera))]
public class AtmosphericScattering : MonoBehaviour
{
    public enum RenderMode
    {
        Reference,
        Optimized
    }

    public enum LightShaftsQuality
    {
        High,
        Medium
    }

    public RenderMode RenderingMode = RenderMode.Optimized;
    public LightShaftsQuality LightShaftQuality = LightShaftsQuality.Medium;
    public ComputeShader ScatteringComputeShader;
    public Light Sun;

    private RenderTexture _particleDensityLUT = null;
    private Texture2D _randomVectorsLUT = null;
    private RenderTexture _lightColorTexture;
    private Texture2D _lightColorTextureTemp;

    private Vector3 _skyboxLUTSize = new Vector3(32, 128, 32);

    private RenderTexture _skyboxLUT;
    private RenderTexture _skyboxLUT2;

    private Vector3 _inscatteringLUTSize = new Vector3(8, 8, 64);
    private RenderTexture _inscatteringLUT;
    private RenderTexture _extinctionLUT;

    private const int LightLUTSize = 128;

    [ColorUsage(false, true, 0, 15, 0, 15)]
    private Color[] _directionalLightLUT;
    [ColorUsage(false, true, 0, 10, 0, 10)]
    private Color[] _ambientLightLUT;

    private Material _material;
    private Material _lightShaftMaterial;
    private Camera _camera;

    private Color _sunColor;

    private Texture2D _ditheringTexture;

    private CommandBuffer _lightShaftsCommandBuffer;
    private CommandBuffer _cascadeShadowCommandBuffer;

    private ReflectionProbe _reflectionProbe;

    [Range(1, 64)]
    public int SampleCount = 16;
    public float MaxRayLength = 400;

    [ColorUsage(false, true, 0, 10, 0, 10)]
    public Color IncomingLight = new Color(4, 4, 4, 4);
    [Range(0, 10.0f)]
    public float RayleighScatterCoef = 1;
    [Range(0, 10.0f)]
    public float RayleighExtinctionCoef = 1;
    [Range(0, 10.0f)]
    public float MieScatterCoef = 1;
    [Range(0, 10.0f)]
    public float MieExtinctionCoef = 1;
    [Range(0.0f, 0.999f)]
    public float MieG = 0.76f;
    public float DistanceScale = 1;

    public bool UpdateLightColor = true;
    [Range(0.5f, 3.0f)]
    public float LightColorIntensity = 1.0f;
    public bool UpdateAmbientColor = true;
    [Range(0.5f, 3.0f)]
    public float AmbientColorIntensity = 1.0f;

    public bool RenderSun = true;
    public float SunIntensity = 1;
    public bool RenderLightShafts = false;
    public bool RenderAtmosphericFog = true;
    public bool ReflectionProbe = true;
    public int ReflectionProbeResolution = 128;

#if UNITY_EDITOR
    public bool GeneralSettingsFoldout = true;
    public bool ScatteringFoldout = true;
    public bool SunFoldout = false;
    public bool LightShaftsFoldout = true;
    public bool AmbientFoldout = false;
    public bool DirLightFoldout = false;
    public bool ReflectionProbeFoldout = false;
    private StringBuilder _stringBuilder = new StringBuilder();
#endif

    private const float AtmosphereHeight = 80000.0f;
    private const float PlanetRadius = 6371000.0f;
    private readonly Vector4 DensityScale = new Vector4(7994.0f, 1200.0f, 0, 0);
    private readonly Vector4 RayleighSct = new Vector4(5.8f, 13.5f, 33.1f, 0.0f) * 0.000001f;
    private readonly Vector4 MieSct = new Vector4(2.0f, 2.0f, 2.0f, 0.0f) * 0.00001f;

    private Vector4[] _FrustumCorners = new Vector4[4];

    /// <summary>
    /// 
    /// </summary>
    void Start()
    {
        Shader shader = Shader.Find("Hidden/AtmosphericScattering");
        if (shader == null)
            throw new Exception("Critical Error: \"Hidden/AtmosphericScattering\" shader is missing. Make sure it is included in \"Always Included Shaders\" in ProjectSettings/Graphics.");
        _material = new Material(shader);

        shader = Shader.Find("Hidden/AtmosphericScattering/LightShafts");
        if (shader == null)
            throw new Exception("Critical Error: \"Hidden/AtmosphericScattering/LightShafts\" shader is missing. Make sure it is included in \"Always Included Shaders\" in ProjectSettings/Graphics.");
        _lightShaftMaterial = new Material(shader);
        
        _camera = GetComponent<Camera>();

        UpdateMaterialParameters(_material);

        //if (_particleDensityLUT == null)
        {
            InitialzieRandomVectorsLUT();
            PrecomputeParticleDensity();
            CalculateLightLUTs();
        }

        InitializeInscatteringLUT();

        GenerateDitherTexture();

        if (RenderLightShafts)
        {
            InitializeLightShafts();
            EnableLightShafts();
        }

        if (ReflectionProbe)
            InitializeReflectionProbe();
    }

    /// <summary>
    /// 
    /// </summary>
    public bool IsInitialized()
    {
        return _material == null ? false : true;
    }

    /// <summary>
    /// 
    /// </summary>
    public void EnableLightShafts()
    {
        if (_lightShaftsCommandBuffer == null)
            InitializeLightShafts();

        Sun.RemoveCommandBuffer(LightEvent.AfterShadowMap, _cascadeShadowCommandBuffer);
        Sun.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, _lightShaftsCommandBuffer);

        Sun.AddCommandBuffer(LightEvent.AfterShadowMap, _cascadeShadowCommandBuffer);
        Sun.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, _lightShaftsCommandBuffer);
    }

    /// <summary>
    /// 
    /// </summary>
    public void DisableLightShafts()
    {
        Sun.RemoveCommandBuffer(LightEvent.AfterShadowMap, _cascadeShadowCommandBuffer);
        Sun.RemoveCommandBuffer(LightEvent.BeforeScreenspaceMask, _lightShaftsCommandBuffer);
    }

    /// <summary>
    /// 
    /// </summary>
    public void EnableReflectionProbe()
    {
        if (_reflectionProbe == null)
            InitializeReflectionProbe();
        _reflectionProbe.gameObject.SetActive(true);
    }

    /// <summary>
    /// 
    /// </summary>
    public void DisableReflectionProbe()
    {
        if (_reflectionProbe != null)
            _reflectionProbe.gameObject.SetActive(false);
    }

    /// <summary>
    /// 
    /// </summary>
    public void ChangeReflectionProbeResolution()
    {
        if (_reflectionProbe != null)
            _reflectionProbe.resolution = ReflectionProbeResolution;
    }

#if UNITY_EDITOR
    /// <summary>
    /// 
    /// </summary>
    public string Validate()
    {
        _stringBuilder.Length = 0;
        if (RenderSettings.skybox == null)
            _stringBuilder.AppendLine("! RenderSettings.skybox is null");
        else if (RenderSettings.skybox.shader.name != "Skybox/AtmosphericScattering")
            _stringBuilder.AppendLine("! RenderSettings.skybox material is using wrong shader");
        if (ScatteringComputeShader == null)
            _stringBuilder.AppendLine("! Atmospheric Scattering compute shader is missing (General Settings)");
        if (Sun == null)
            _stringBuilder.AppendLine("! Sun (main directional light) isn't set (General Settings)");
        else if (RenderLightShafts && Sun.shadows == LightShadows.None)
            _stringBuilder.AppendLine("! Light shafts are enabled but sun light doesn't cast shadows");
        if (RenderLightShafts == true && RenderAtmosphericFog == false)
            _stringBuilder.AppendLine("! Light shafts are enabled but atm. fog isn't");

        return _stringBuilder.ToString();
    }
#endif

    /// <summary>
    /// 
    /// </summary>
    private void InitializeReflectionProbe()
    {
        if (_reflectionProbe != null)
            return;

        GameObject go = new GameObject("ReflectionProbe");
        go.transform.parent = _camera.transform;
        go.transform.position = new Vector3(0, 0, 0);

        _reflectionProbe = go.AddComponent<ReflectionProbe>();

        _reflectionProbe.clearFlags = ReflectionProbeClearFlags.Skybox;
        _reflectionProbe.cullingMask = 0;
        _reflectionProbe.hdr = true;
        _reflectionProbe.mode = ReflectionProbeMode.Realtime;
        _reflectionProbe.refreshMode = ReflectionProbeRefreshMode.EveryFrame;
        _reflectionProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
        _reflectionProbe.resolution = 128;
        _reflectionProbe.size = new Vector3(100000, 100000, 100000);
    }

    /// <summary>
    /// 
    /// </summary>
    public void InitializeLightShafts()
    {
        if (_cascadeShadowCommandBuffer == null)
        {
            _cascadeShadowCommandBuffer = new CommandBuffer();
            _cascadeShadowCommandBuffer.name = "CascadeShadowCommandBuffer";
            _cascadeShadowCommandBuffer.SetGlobalTexture("_CascadeShadowMapTexture", new UnityEngine.Rendering.RenderTargetIdentifier(UnityEngine.Rendering.BuiltinRenderTextureType.CurrentActive));
        }

        if (_lightShaftsCommandBuffer == null)
        {
            _lightShaftsCommandBuffer = new CommandBuffer();
            _lightShaftsCommandBuffer.name = "LightShaftsCommandBuffer";
        }
        else
        {
            _lightShaftsCommandBuffer.Clear();
        }

        int lightShaftsRT1 = Shader.PropertyToID("_LightShaft1");
        int lightShaftsRT2 = Shader.PropertyToID("_LightShaft2");
        int halfDepthBuffer = Shader.PropertyToID("_HalfResDepthBuffer");
        int halfShaftsRT1 = Shader.PropertyToID("_HalfResColor");
        int halfShaftsRT2 = Shader.PropertyToID("_HalfResColorTemp");

        Texture nullTexture = null;
        if (LightShaftQuality == LightShaftsQuality.High)
        {
            _lightShaftsCommandBuffer.GetTemporaryRT(lightShaftsRT1, _camera.pixelWidth, _camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.RHalf);
            _lightShaftsCommandBuffer.Blit(nullTexture, new RenderTargetIdentifier(lightShaftsRT1), _lightShaftMaterial, 10);

            _lightShaftsCommandBuffer.GetTemporaryRT(lightShaftsRT2, _camera.pixelWidth, _camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.RHalf);
            // horizontal bilateral blur
            _lightShaftsCommandBuffer.Blit(new RenderTargetIdentifier(lightShaftsRT1), new RenderTargetIdentifier(lightShaftsRT2), _lightShaftMaterial, 0);
            // vertical bilateral blur
            _lightShaftsCommandBuffer.Blit(new RenderTargetIdentifier(lightShaftsRT2), new RenderTargetIdentifier(lightShaftsRT1), _lightShaftMaterial, 1);
        }
        else if (LightShaftQuality == LightShaftsQuality.Medium)
        {
            _lightShaftsCommandBuffer.GetTemporaryRT(lightShaftsRT1, _camera.pixelWidth, _camera.pixelHeight, 0, FilterMode.Bilinear, RenderTextureFormat.RHalf);
            _lightShaftsCommandBuffer.GetTemporaryRT(halfDepthBuffer, _camera.pixelWidth / 2, _camera.pixelHeight / 2, 0, FilterMode.Point, RenderTextureFormat.RFloat);
            _lightShaftsCommandBuffer.GetTemporaryRT(halfShaftsRT1, _camera.pixelWidth / 2, _camera.pixelHeight / 2, 0, FilterMode.Bilinear, RenderTextureFormat.RHalf);
            _lightShaftsCommandBuffer.GetTemporaryRT(halfShaftsRT2, _camera.pixelWidth / 2, _camera.pixelHeight / 2, 0, FilterMode.Bilinear, RenderTextureFormat.RHalf);
            
            // down sample depth to half res
            _lightShaftsCommandBuffer.Blit(nullTexture, new RenderTargetIdentifier(halfDepthBuffer), _lightShaftMaterial, 4);
            _lightShaftsCommandBuffer.Blit(nullTexture, new RenderTargetIdentifier(halfShaftsRT1), _lightShaftMaterial, 10);

            // horizontal bilateral blur at full res
            _lightShaftsCommandBuffer.Blit(new RenderTargetIdentifier(halfShaftsRT1), new RenderTargetIdentifier(halfShaftsRT2), _lightShaftMaterial, 2);
            // vertical bilateral blur at full res
            _lightShaftsCommandBuffer.Blit(new RenderTargetIdentifier(halfShaftsRT2), new RenderTargetIdentifier(halfShaftsRT1), _lightShaftMaterial, 3);

            // upscale to full res
            _lightShaftsCommandBuffer.Blit(new RenderTargetIdentifier(halfShaftsRT1), new RenderTargetIdentifier(lightShaftsRT1), _lightShaftMaterial, 5);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void InitializeInscatteringLUT()
    {
        _inscatteringLUT = new RenderTexture((int)_inscatteringLUTSize.x, (int)_inscatteringLUTSize.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        _inscatteringLUT.volumeDepth = (int)_inscatteringLUTSize.z;
        _inscatteringLUT.isVolume = true;
        _inscatteringLUT.enableRandomWrite = true;
        _inscatteringLUT.name = "InscatteringLUT";
        _inscatteringLUT.Create();

        _extinctionLUT = new RenderTexture((int)_inscatteringLUTSize.x, (int)_inscatteringLUTSize.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        _extinctionLUT.volumeDepth = (int)_inscatteringLUTSize.z;
        _extinctionLUT.isVolume = true;
        _extinctionLUT.enableRandomWrite = true;
        _extinctionLUT.name = "ExtinctionLUT";
        _extinctionLUT.Create();
    }

    /// <summary>
    /// 
    /// </summary>
    private void PrecomputeSkyboxLUT()
    {
        if (_skyboxLUT == null)
        {
            _skyboxLUT = new RenderTexture((int)_skyboxLUTSize.x, (int)_skyboxLUTSize.y, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            _skyboxLUT.volumeDepth = (int)_skyboxLUTSize.z;
            _skyboxLUT.isVolume = true;
            _skyboxLUT.enableRandomWrite = true;
            _skyboxLUT.name = "SkyboxLUT";
            _skyboxLUT.Create();
        }

#if HIGH_QUALITY
        if (_skyboxLUT2 == null)
        {
            _skyboxLUT2 = new RenderTexture((int)_skyboxLUTSize.x, (int)_skyboxLUTSize.y, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear);
            _skyboxLUT2.volumeDepth = (int)_skyboxLUTSize.z;
            _skyboxLUT2.isVolume = true;
            _skyboxLUT2.enableRandomWrite = true;
            _skyboxLUT2.name = "SkyboxLUT2";
            _skyboxLUT2.Create();
        }
#endif

        int kernel = ScatteringComputeShader.FindKernel("SkyboxLUT");

        ScatteringComputeShader.SetTexture(kernel, "_SkyboxLUT", _skyboxLUT);
#if HIGH_QUALITY
        ScatteringComputeShader.SetTexture(kernel, "_SkyboxLUT2", _skyboxLUT2);
#endif

        UpdateCommonComputeShaderParameters(kernel);

        ScatteringComputeShader.Dispatch(kernel, (int)_skyboxLUTSize.x, (int)_skyboxLUTSize.y, (int)_skyboxLUTSize.z);
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateCommonComputeShaderParameters(int kernel)
    {
        ScatteringComputeShader.SetTexture(kernel, "_ParticleDensityLUT", _particleDensityLUT);
        ScatteringComputeShader.SetFloat("_AtmosphereHeight", AtmosphereHeight);
        ScatteringComputeShader.SetFloat("_PlanetRadius", PlanetRadius);
        ScatteringComputeShader.SetVector("_DensityScaleHeight", DensityScale);

        ScatteringComputeShader.SetVector("_ScatteringR", RayleighSct * RayleighScatterCoef);
        ScatteringComputeShader.SetVector("_ScatteringM", MieSct * MieScatterCoef);
        ScatteringComputeShader.SetVector("_ExtinctionR", RayleighSct * RayleighExtinctionCoef);
        ScatteringComputeShader.SetVector("_ExtinctionM", MieSct * MieExtinctionCoef);

        ScatteringComputeShader.SetVector("_IncomingLight", IncomingLight);
        ScatteringComputeShader.SetFloat("_MieG", MieG);
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateInscatteringLUT()
    {
        int kernel = ScatteringComputeShader.FindKernel("InscatteringLUT");

        ScatteringComputeShader.SetTexture(kernel, "_InscatteringLUT", _inscatteringLUT);
        ScatteringComputeShader.SetTexture(kernel, "_ExtinctionLUT", _extinctionLUT);

        ScatteringComputeShader.SetVector("_InscatteringLUTSize", _inscatteringLUTSize);

        ScatteringComputeShader.SetVector("_BottomLeftCorner", _FrustumCorners[0]);        
        ScatteringComputeShader.SetVector("_TopLeftCorner", _FrustumCorners[1]);
        ScatteringComputeShader.SetVector("_TopRightCorner", _FrustumCorners[2]);
        ScatteringComputeShader.SetVector("_BottomRightCorner", _FrustumCorners[3]);

        ScatteringComputeShader.SetVector("_CameraPos", transform.position);
        ScatteringComputeShader.SetVector("_LightDir", Sun.transform.forward);
        ScatteringComputeShader.SetFloat("_DistanceScale", DistanceScale);

        UpdateCommonComputeShaderParameters(kernel);

        ScatteringComputeShader.Dispatch(kernel, (int)_inscatteringLUTSize.x, (int)_inscatteringLUTSize.y, 1);
    }

    /// <summary>
    /// 
    /// </summary>
    public void OnDestroy()
    {
        Destroy(_material);
        Destroy(_lightShaftMaterial);
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateMaterialParameters(Material material)
    {
        material.SetFloat("_AtmosphereHeight", AtmosphereHeight);
        material.SetFloat("_PlanetRadius", PlanetRadius);
        material.SetVector("_DensityScaleHeight", DensityScale);

        Vector4 scatteringR = new Vector4(5.8f, 13.5f, 33.1f, 0.0f) * 0.000001f;
        Vector4 scatteringM = new Vector4(2.0f, 2.0f, 2.0f, 0.0f) * 0.00001f;
        material.SetVector("_ScatteringR", RayleighSct * RayleighScatterCoef);
        material.SetVector("_ScatteringM", MieSct * MieScatterCoef);
        material.SetVector("_ExtinctionR", RayleighSct * RayleighExtinctionCoef);
        material.SetVector("_ExtinctionM", MieSct * MieExtinctionCoef);

        material.SetColor("_IncomingLight", IncomingLight);
        material.SetFloat("_MieG", MieG);
        material.SetFloat("_DistanceScale", DistanceScale);
        material.SetColor("_SunColor", _sunColor);

        //---------------------------------------------------

        material.SetVector("_LightDir", new Vector4(Sun.transform.forward.x, Sun.transform.forward.y, Sun.transform.forward.z, 1.0f / (Sun.range * Sun.range)));
        material.SetVector("_LightColor", Sun.color * Sun.intensity);

        material.SetTexture("_ParticleDensityLUT", _particleDensityLUT);

        material.SetTexture("_SkyboxLUT", _skyboxLUT);
        material.SetTexture("_SkyboxLUT2", _skyboxLUT2);
    }

    /// <summary>
    /// 
    /// </summary>
    public void CalculateLightLUTs()
    {
        if (_lightColorTexture == null)
        {
            _lightColorTexture = new RenderTexture(LightLUTSize, 1, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
            _lightColorTexture.name = "LightColorTexture";
            _lightColorTexture.Create();
        }

        if (_lightColorTextureTemp == null)
        {
            _lightColorTextureTemp = new Texture2D(LightLUTSize, 1, TextureFormat.RGBAHalf, false, true);
            _lightColorTextureTemp.name = "LightColorTextureTemp";
            _lightColorTextureTemp.Apply();
        }

        // ambient LUT
        Texture nullTexture = null;
        _material.SetTexture("_RandomVectors", _randomVectorsLUT);
        Graphics.Blit(nullTexture, _lightColorTexture, _material, 1);

        _lightColorTextureTemp.ReadPixels(new Rect(0, 0, LightLUTSize, 1), 0, 0);
        _ambientLightLUT = _lightColorTextureTemp.GetPixels(0, 0, LightLUTSize, 1);

        // directional LUT
        Graphics.Blit(nullTexture, _lightColorTexture, _material, 2);

        _lightColorTextureTemp.ReadPixels(new Rect(0, 0, LightLUTSize, 1), 0, 0);
        _directionalLightLUT = _lightColorTextureTemp.GetPixels(0, 0, LightLUTSize, 1);

        PrecomputeSkyboxLUT();
    }

    /// <summary>
    /// 
    /// </summary>
    private void InitialzieRandomVectorsLUT()
    {
        _randomVectorsLUT = new Texture2D(256, 1, TextureFormat.RGBAHalf, false, true);
        _randomVectorsLUT.name = "RandomVectorsLUT";
        Color[] colors = new Color[256];
        UnityEngine.Random.seed = 1234567890;
        for (int i = 0; i < colors.Length; ++i)
        {
            Vector3 vector = UnityEngine.Random.onUnitSphere;
            colors[i] = new Color(vector.x, vector.y, vector.z, 1);
        }

        _randomVectorsLUT.SetPixels(colors);

        _randomVectorsLUT.Apply();
    }

    /// <summary>
    /// 
    /// </summary>
    private void PrecomputeParticleDensity()
    {
        if (_particleDensityLUT == null)
        {
            _particleDensityLUT = new RenderTexture(1024, 1024, 0, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear);
            _particleDensityLUT.name = "ParticleDensityLUT";
            _particleDensityLUT.filterMode = FilterMode.Bilinear;
            _particleDensityLUT.Create();
        }

        Texture nullTexture = null;
        Graphics.Blit(nullTexture, _particleDensityLUT, _material, 0);

        _material.SetTexture("_ParticleDensityLUT", _particleDensityLUT);
    }

    /// <summary>
    /// 
    /// </summary>
    private Color ComputeLightColor()
    {
        float cosAngle = Vector3.Dot(Vector3.up, -Sun.transform.forward);
        float u = (cosAngle + 0.1f) / 1.1f;// * 0.5f + 0.5f;

        u = u * LightLUTSize;
        int index0 = Mathf.FloorToInt(u);
        float weight1 = u - index0;
        int index1 = index0 + 1;
        float weight0 = 1 - weight1;

        index0 = Mathf.Clamp(index0, 0, LightLUTSize - 1);
        index1 = Mathf.Clamp(index1, 0, LightLUTSize - 1);

        Color c = _directionalLightLUT[index0] * weight0 + _directionalLightLUT[index1] * weight1;
        return c.gamma;
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateDirectionalLightColor(Color c)
    {
        Vector3 color = new Vector3(c.r, c.g, c.b);
        float length = color.magnitude;
        color /= length;

        Sun.color = new Color(Mathf.Max(color.x, 0.01f), Mathf.Max(color.y, 0.01f), Mathf.Max(color.z, 0.01f), 1);
        Sun.intensity = Mathf.Max(length, 0.01f) * LightColorIntensity; // make sure unity doesn't disable this light
    }

    /// <summary>
    /// 
    /// </summary>
    private Color ComputeAmbientColor()
    {
        float cosAngle = Vector3.Dot(Vector3.up, -Sun.transform.forward);
        float u = (cosAngle + 0.1f) / 1.1f;// * 0.5f + 0.5f;

        u = u * LightLUTSize;
        int index0 = Mathf.FloorToInt(u);
        float weight1 = u - index0;
        int index1 = index0 + 1;
        float weight0 = 1 - weight1;

        index0 = Mathf.Clamp(index0, 0, LightLUTSize - 1);
        index1 = Mathf.Clamp(index1, 0, LightLUTSize - 1);

        Color c = _ambientLightLUT[index0] * weight0 + _ambientLightLUT[index1] * weight1;
        return c.gamma;
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateAmbientLightColor(Color c)
    {
#if UNITY_5_4_OR_NEWER
        RenderSettings.ambientLight = c * AmbientColorIntensity;
#else
        Vector3 color = new Vector3(c.r, c.g, c.b);
        float length = color.magnitude;
        color /= length;

        RenderSettings.ambientLight = new Color(color.x, color.y, color.z, 1);
        RenderSettings.ambientIntensity = Mathf.Max(length, 0.01f) * AmbientColorIntensity;
#endif
    }

    /// <summary>
    /// 
    /// </summary>
    void Update()
    {
        _sunColor = ComputeLightColor();
        if (UpdateLightColor)
            UpdateDirectionalLightColor(_sunColor);
        if (UpdateAmbientColor)
            UpdateAmbientLightColor(ComputeAmbientColor());
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateSkyBoxParameters()
    {
        if (RenderSettings.skybox != null)
        {
            RenderSettings.skybox.SetVector("_CameraPos", _camera.transform.position);
            UpdateMaterialParameters(RenderSettings.skybox);
            if (RenderingMode == RenderMode.Reference)
                RenderSettings.skybox.EnableKeyword("ATMOSPHERE_REFERENCE");
            else
                RenderSettings.skybox.DisableKeyword("ATMOSPHERE_REFERENCE");

            RenderSettings.skybox.SetFloat("_SunIntensity", SunIntensity);
            if (RenderSun)
                RenderSettings.skybox.EnableKeyword("RENDER_SUN");
            else
                RenderSettings.skybox.DisableKeyword("RENDER_SUN");

            //RenderSettings.skybox.EnableKeyword("HIGH_QUALITY");
        }
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateLightShaftsParameters()
    {
#if UNITY_5_4_OR_NEWER
        _lightShaftMaterial.SetVectorArray("_FrustumCorners", _FrustumCorners);
#else
        _lightShaftMaterial.SetVector("_FrustumCorners0", _FrustumCorners[0]);
        _lightShaftMaterial.SetVector("_FrustumCorners1", _FrustumCorners[1]);
        _lightShaftMaterial.SetVector("_FrustumCorners2", _FrustumCorners[2]);
        _lightShaftMaterial.SetVector("_FrustumCorners3", _FrustumCorners[3]);
#endif

        _lightShaftMaterial.SetInt("_SampleCount", SampleCount);
        _lightShaftMaterial.SetTexture("_DitherTexture", _ditheringTexture);

        _lightShaftMaterial.SetVector("_FullResTexelSize", new Vector4(1.0f / _camera.pixelWidth, 1.0f / _camera.pixelHeight, 0, 0));
        _lightShaftMaterial.SetVector("_HalfResTexelSize", new Vector4(1.0f / (_camera.pixelWidth * 0.5f), 1.0f / (_camera.pixelHeight * 0.5f), 0, 0));
    }

    /// <summary>
    /// 
    /// </summary>
    private void UpdateLightScatteringParameters()
    {
        UpdateMaterialParameters(_material);

#if UNITY_5_4_OR_NEWER
        _material.SetVectorArray("_FrustumCorners", _FrustumCorners);
#else
        _material.SetVector("_FrustumCorners0", _FrustumCorners[0]);
        _material.SetVector("_FrustumCorners1", _FrustumCorners[1]);
        _material.SetVector("_FrustumCorners2", _FrustumCorners[2]);
        _material.SetVector("_FrustumCorners3", _FrustumCorners[3]);
#endif

        _material.SetFloat("_SunIntensity", SunIntensity);

        _material.SetTexture("_InscatteringLUT", _inscatteringLUT);
        _material.SetTexture("_ExtinctionLUT", _extinctionLUT);

        if (RenderingMode == RenderMode.Reference)
            _material.EnableKeyword("ATMOSPHERE_REFERENCE");
        else
            _material.DisableKeyword("ATMOSPHERE_REFERENCE");

        if (RenderLightShafts)
            _material.EnableKeyword("LIGHT_SHAFTS");
        else
            _material.DisableKeyword("LIGHT_SHAFTS");
    }

    /// <summary>
    /// 
    /// </summary>
    public void OnPreRender()
    {
        // get four corners of camera frustom in world space
        // bottom left
        _FrustumCorners[0] = _camera.ViewportToWorldPoint(new Vector3(0, 0, _camera.farClipPlane));        
        // top left
        _FrustumCorners[1] = _camera.ViewportToWorldPoint(new Vector3(0, 1, _camera.farClipPlane));
        // top right
        _FrustumCorners[2] = _camera.ViewportToWorldPoint(new Vector3(1, 1, _camera.farClipPlane));
        // bottom right
        _FrustumCorners[3] = _camera.ViewportToWorldPoint(new Vector3(1, 0, _camera.farClipPlane));

        // update parameters
        UpdateSkyBoxParameters();
        UpdateLightScatteringParameters();
        UpdateLightShaftsParameters();

        UpdateInscatteringLUT();
    }

    /// <summary>
    /// 
    /// </summary>
    [ImageEffectOpaque]
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (!RenderAtmosphericFog)
        {
            Graphics.Blit(source, destination);
            return;
        }


        //Graphics.SetRenderTarget(destination);
        //_material.SetPass(3);
        _material.SetTexture("_Background", source);

        //Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
        Texture nullTexture = null;
        Graphics.Blit(nullTexture, destination, _material, 3);
    }
    
    /// <summary>
    /// 
    /// </summary>
    private void GenerateDitherTexture()
    {
        if (_ditheringTexture != null)
        {
            return;
        }

        int size = 8;
#if DITHER_4_4
        size = 4;
#endif
        // again, I couldn't make it work with Alpha8
        _ditheringTexture = new Texture2D(size, size, TextureFormat.Alpha8, false, true);
        _ditheringTexture.filterMode = FilterMode.Point;
        Color32[] c = new Color32[size * size];

        byte b;
#if DITHER_4_4
        b = (byte)(0.0f / 16.0f * 255); c[0] = new Color32(b, b, b, b);
        b = (byte)(8.0f / 16.0f * 255); c[1] = new Color32(b, b, b, b);
        b = (byte)(2.0f / 16.0f * 255); c[2] = new Color32(b, b, b, b);
        b = (byte)(10.0f / 16.0f * 255); c[3] = new Color32(b, b, b, b);

        b = (byte)(12.0f / 16.0f * 255); c[4] = new Color32(b, b, b, b);
        b = (byte)(4.0f / 16.0f * 255); c[5] = new Color32(b, b, b, b);
        b = (byte)(14.0f / 16.0f * 255); c[6] = new Color32(b, b, b, b);
        b = (byte)(6.0f / 16.0f * 255); c[7] = new Color32(b, b, b, b);

        b = (byte)(3.0f / 16.0f * 255); c[8] = new Color32(b, b, b, b);
        b = (byte)(11.0f / 16.0f * 255); c[9] = new Color32(b, b, b, b);
        b = (byte)(1.0f / 16.0f * 255); c[10] = new Color32(b, b, b, b);
        b = (byte)(9.0f / 16.0f * 255); c[11] = new Color32(b, b, b, b);

        b = (byte)(15.0f / 16.0f * 255); c[12] = new Color32(b, b, b, b);
        b = (byte)(7.0f / 16.0f * 255); c[13] = new Color32(b, b, b, b);
        b = (byte)(13.0f / 16.0f * 255); c[14] = new Color32(b, b, b, b);
        b = (byte)(5.0f / 16.0f * 255); c[15] = new Color32(b, b, b, b);
#else
        int i = 0;
        b = (byte)(1.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(49.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(13.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(61.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(4.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(52.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(16.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(64.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);

        b = (byte)(33.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(17.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(45.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(29.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(36.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(20.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(48.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(32.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);

        b = (byte)(9.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(57.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(5.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(53.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(12.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(60.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(8.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(56.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);

        b = (byte)(41.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(25.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(37.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(21.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(44.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(28.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(40.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(24.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);

        b = (byte)(3.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(51.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(15.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(63.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(2.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(50.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(14.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(62.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);

        b = (byte)(35.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(19.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(47.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(31.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(34.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(18.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(46.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(30.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);

        b = (byte)(11.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(59.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(7.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(55.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(10.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(58.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(6.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(54.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);

        b = (byte)(43.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(27.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(39.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(23.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(42.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(26.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(38.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
        b = (byte)(22.0f / 65.0f * 255); c[i++] = new Color32(b, b, b, b);
#endif

        _ditheringTexture.SetPixels32(c);
        _ditheringTexture.Apply();
    }
}
