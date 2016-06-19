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

[RequireComponent(typeof(Light))]
public class AtmosphericScattering : MonoBehaviour
{
    private /*static*/ RenderTexture _particleDensityLUT = null;
    private Texture2D _randomVectorsLUT = null;
    private RenderTexture _lightColorTexture;
    private Texture2D _lightColorTextureTemp;

    private const int LightLUTSize = 128;

    [ColorUsage(false, true, 0, 15, 0, 15)]
    private Color[] _directionalLightLUT;
    [ColorUsage(false, true, 0, 10, 0, 10)]
    private Color[] _ambientLightLUT;

    private Light _light;
    private Material _material;
    private CommandBuffer _commandBuffer;
    private CommandBuffer _cascadeShadowCommandBuffer;

    [Range(1, 64)]
    public int SampleCount = 16;
    
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
    public float DistanceMultiplier = 1;

    public bool UpdateLightColor = true;
    [Range(0.5f, 3.0f)]
    public float LightColorIntensity = 1.0f;
    public bool UpdateAmbientColor = true;
    [Range(0.5f, 3.0f)]
    public float AmbientColorIntensity = 1.0f;

    /// <summary>
    /// 
    /// </summary>
    void Start()
    {
        _commandBuffer = new CommandBuffer();
        _commandBuffer.name = "Light Command Buffer";

        _cascadeShadowCommandBuffer = new CommandBuffer();
        _cascadeShadowCommandBuffer.name = "Dir Light Command Buffer";
        _cascadeShadowCommandBuffer.SetGlobalTexture("_CascadeShadowMapTexture", new UnityEngine.Rendering.RenderTargetIdentifier(UnityEngine.Rendering.BuiltinRenderTextureType.CurrentActive));

        _light = GetComponent<Light>();
        _light.RemoveAllCommandBuffers();
        if (_light.type == LightType.Directional)
        {
            _light.AddCommandBuffer(LightEvent.BeforeScreenspaceMask, _commandBuffer);
            _light.AddCommandBuffer(LightEvent.AfterShadowMap, _cascadeShadowCommandBuffer);

        }
        else
            _light.AddCommandBuffer(LightEvent.AfterShadowMap, _commandBuffer);

        _material = new Material(Shader.Find("Sandbox/AtmosphericScattering"));

        UpdateMaterialParameters();

        //if (_particleDensityLUT == null)
        {
            InitialzieRandomVectorsLUT();
            PrecomputeParticleDensity();
            CalculateLightLUTs();
        }
    }

    /// <summary>
    /// 
    /// </summary>
    void OnEnable()
    {
        AtmosphericScatteringRenderer.PreRenderEvent += AtmosphericScatteringRenderer_PreRenderEvent;
    }

    /// <summary>
    /// 
    /// </summary>
    void OnDisable()
    {
        AtmosphericScatteringRenderer.PreRenderEvent -= AtmosphericScatteringRenderer_PreRenderEvent;
    }

    /// <summary>
    /// 
    /// </summary>
    public void OnDestroy()
    {
        Destroy(_material);
    }

    private void UpdateMaterialParameters()
    {
        _material.SetInt("_SampleCount", SampleCount);
        //_material.SetTexture("_CameraDepthTexture", renderer.GetVolumeLightDepthBuffer());        
        _material.SetFloat("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        _material.SetFloat("_AtmosphereHeight", 80000.0f);
        _material.SetFloat("_PlanetRadius", 6371000.0f);
        _material.SetVector("_DensityScaleHeight", new Vector4(7994.0f, 1200.0f, 0, 0));

        Vector4 scatteringR = new Vector4(5.8f, 13.5f, 33.1f, 0.0f) * 0.000001f;
        Vector4 scatteringM = new Vector4(2.0f, 2.0f, 2.0f, 0.0f) * 0.00001f;
        _material.SetVector("_ScatteringR", scatteringR * RayleighScatterCoef);
        _material.SetVector("_ScatteringM", scatteringM * MieScatterCoef);
        _material.SetVector("_ExtinctionR", scatteringR * RayleighExtinctionCoef);
        _material.SetVector("_ExtinctionM", scatteringM * MieExtinctionCoef);

        _material.SetColor("_IncomingLight", IncomingLight);
        _material.SetFloat("_MieG", MieG);
        _material.SetFloat("_DistanceMultiplier", DistanceMultiplier);

        //---------------------------------------------------

        _material.SetVector("_LightDir", new Vector4(_light.transform.forward.x, _light.transform.forward.y, _light.transform.forward.z, 1.0f / (_light.range * _light.range)));
        _material.SetVector("_LightColor", _light.color * _light.intensity);

        _material.SetTexture("_ParticleDensityLUT", _particleDensityLUT);

        if (_light.cookie == null)
        {
            _material.EnableKeyword("DIRECTIONAL");
            _material.DisableKeyword("DIRECTIONAL_COOKIE");
        }
        else
        {
            _material.EnableKeyword("DIRECTIONAL_COOKIE");
            _material.DisableKeyword("DIRECTIONAL");

            _material.SetTexture("_LightTexture0", _light.cookie);
        }
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="renderer"></param>
    /// <param name="viewProj"></param>
    private void AtmosphericScatteringRenderer_PreRenderEvent(AtmosphericScatteringRenderer renderer, Matrix4x4 viewProj)
    {
        if (!_light.gameObject.activeInHierarchy)
            return;

        UpdateMaterialParameters();

        float zScale = Camera.current.farClipPlane * 0.98f;
        float yScale = Camera.current.farClipPlane * Mathf.Tan(Mathf.Deg2Rad * Camera.current.fieldOfView * 0.5f);
        float xScale = yScale * Camera.current.aspect;
        Matrix4x4 world = Matrix4x4.TRS(Camera.current.transform.position, Camera.current.transform.rotation, new Vector3(xScale, yScale, zScale));
        
        _material.SetVector("_CameraForward", Camera.current.transform.forward);
        _material.SetMatrix("_WorldViewProj", viewProj * world);
        _material.SetMatrix("_WorldView", Camera.current.worldToCameraMatrix * world);

        if (_light.type == LightType.Directional)
        {
            SetupDirectionalLight(renderer, world);
        }
    }

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
        Graphics.Blit(nullTexture, _lightColorTexture, _material, 2);       

        _lightColorTextureTemp.ReadPixels(new Rect(0, 0, LightLUTSize, 1), 0, 0);
        _ambientLightLUT = _lightColorTextureTemp.GetPixels(0, 0, LightLUTSize, 1);

        // directional LUT
        Graphics.Blit(nullTexture, _lightColorTexture, _material, 3);

        _lightColorTextureTemp.ReadPixels(new Rect(0, 0, LightLUTSize, 1), 0, 0);
        _directionalLightLUT = _lightColorTextureTemp.GetPixels(0, 0, LightLUTSize, 1);
    }

    private void InitialzieRandomVectorsLUT()
    {
        _randomVectorsLUT = new Texture2D(256, 1, TextureFormat.RGBAHalf, false, true);
        _randomVectorsLUT.name = "RandomVectorsLUT";
        Color[] colors = new Color[256];
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
        }

        Texture nullTexture = null;
        Graphics.Blit(nullTexture, _particleDensityLUT, _material, 0);

        _material.SetTexture("_ParticleDensityLUT", _particleDensityLUT);
    }

    private Color ComputeLightColor()
    {
        float cosAngle = Vector3.Dot(Vector3.up, -transform.forward);
        float u = cosAngle * 0.5f + 0.5f;

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

    private void UpdateDirectionalLightColor(Color c)
    {
        Vector3 color = new Vector3(c.r, c.g, c.b);
        float length = color.magnitude;
        color /= length;

        _light.color = new Color(Mathf.Max(color.x, 0.01f), Mathf.Max(color.y, 0.01f), Mathf.Max(color.z, 0.01f), 1);
        _light.intensity = Mathf.Max(length, 0.01f) * LightColorIntensity; // make sure unity doesn't disable this light
    }

    private Color ComputeAmbientColor()
    {
        float cosAngle = Vector3.Dot(Vector3.up, -transform.forward);
        float u = cosAngle * 0.5f + 0.5f;

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

    private void UpdateAmbientLightColor(Color c)
    {
        Vector3 color = new Vector3(c.r, c.g, c.b);
        float length = color.magnitude;
        color /= length;

        RenderSettings.ambientLight = new Color(color.x, color.y, color.z, 1);
        RenderSettings.ambientIntensity = Mathf.Max(length, 0.01f) * AmbientColorIntensity;
    }

    void Update()
    {
        _commandBuffer.Clear();

        if(UpdateLightColor)
            UpdateDirectionalLightColor(ComputeLightColor());
        if(UpdateAmbientColor)
            UpdateAmbientLightColor(ComputeAmbientColor());
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="renderer"></param>
    /// <param name="viewProj"></param>
    private void SetupDirectionalLight(AtmosphericScatteringRenderer renderer, Matrix4x4 world)
    {
        int pass = 1;

        _material.SetPass(pass);

        Mesh mesh = AtmosphericScatteringRenderer.GetDirLightMesh();
        
        Texture texture = null;
        if (_light.shadows != LightShadows.None)
        {
            _material.EnableKeyword("SHADOWS_DEPTH");
            _commandBuffer.SetRenderTarget(new RenderTargetIdentifier[] { renderer.GetInscatteringBuffer(), renderer.GetExtinctionBuffer() }, BuiltinRenderTextureType.CurrentActive);
            _commandBuffer.DrawMesh(mesh, world, _material, 0, pass);
        }
        else
        {
            _material.DisableKeyword("SHADOWS_DEPTH");
            renderer.GlobalCommandBuffer.DrawMesh(mesh, world, _material, 0, pass);
        }
    }
}
