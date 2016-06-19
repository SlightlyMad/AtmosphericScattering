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

[RequireComponent(typeof(Camera))]
public class AtmosphericScatteringRenderer : MonoBehaviour
{
    public static event Action<AtmosphericScatteringRenderer, Matrix4x4> PreRenderEvent;

    private static Mesh _dirLightMesh;

    private Camera _camera;
    private CommandBuffer _preLightPass;
    private CommandBuffer _postLightPass;
    private CommandBuffer _preFinalPass;

    private Matrix4x4 _viewProj;
    private Material _material;

    private RenderTexture _inscatteringTexture;
    private RenderTexture _extinctionTexture;

    public CommandBuffer GlobalCommandBuffer { get { return _preLightPass; } }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public static Mesh GetDirLightMesh()
    {
        return _dirLightMesh;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    public RenderTexture GetInscatteringBuffer()
    {
        return _inscatteringTexture;
    }

    public RenderTexture GetExtinctionBuffer()
    {
        return _extinctionTexture;
    }

    /// <summary>
    /// 
    /// </summary>
    void Awake()
    {
        //Application.targetFrameRate = 1000;
        _camera = GetComponent<Camera>();

        _material = new Material(Shader.Find("Sandbox/AtmosphericScattering"));

        _preLightPass = new CommandBuffer();
        _preLightPass.name = "PreLight";

        _postLightPass = new CommandBuffer();
        _postLightPass.name = "PostLight";

        _preFinalPass = new CommandBuffer();
        _preFinalPass.name = "PreFinal";

        if (_dirLightMesh == null)
        {
            _dirLightMesh = CreateDirLightMesh();
        }

        _preFinalPass.Clear();
        // add to Unity's light buffer
        //_preFinalPass.Blit(BuiltinRenderTextureType.None, BuiltinRenderTextureType.CurrentActive, _material, 4);
    }

    /// <summary>
    /// 
    /// </summary>
    void OnEnable()
    {
        //_camera.RemoveAllCommandBuffers();
        _camera.AddCommandBuffer(CameraEvent.BeforeLighting, _preLightPass);
        //_camera.AddCommandBuffer(CameraEvent.AfterLighting, _postLightPass);
        _camera.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _preFinalPass);
    }

    /// <summary>
    /// 
    /// </summary>
    void OnDisable()
    {
        //_camera.RemoveAllCommandBuffers();
        _camera.RemoveCommandBuffer(CameraEvent.BeforeLighting, _preLightPass);
        //_camera.RemoveCommandBuffer(CameraEvent.AfterLighting, _postLightPass);
        _camera.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, _preFinalPass);
    }

    [ImageEffectOpaque]
    public void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(source, destination, _material, 4);
    }
    
    /// <summary>
    /// 
    /// </summary>
    public void OnPreRender()
    {
        Matrix4x4 proj = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, true);

        _viewProj = proj * _camera.worldToCameraMatrix;

        _inscatteringTexture = RenderTexture.GetTemporary(_camera.pixelWidth, _camera.pixelHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
        _extinctionTexture = RenderTexture.GetTemporary(_camera.pixelWidth, _camera.pixelHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);

        _material.SetTexture("_InscatteringTexture", _inscatteringTexture);
        _material.SetTexture("_ExtionctionTexture", _extinctionTexture);

        _preLightPass.Clear();
        _preLightPass.SetRenderTarget(new RenderTargetIdentifier[] { _inscatteringTexture, _extinctionTexture }, BuiltinRenderTextureType.CurrentActive);
        _preLightPass.ClearRenderTarget(false, true, new Color(0, 0, 0, 1));

        if (PreRenderEvent != null)
            PreRenderEvent(this, _viewProj);
    }

    public void OnPostRender()
    {
        RenderTexture.ReleaseTemporary(_inscatteringTexture);
        RenderTexture.ReleaseTemporary(_extinctionTexture);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <returns></returns>
    private Mesh CreateDirLightMesh()
    {
        Mesh mesh = new Mesh();
        Vector3[] vertices = new Vector3[4];

        vertices[0] = new Vector3(-1, -1, 1);
        vertices[1] = new Vector3(-1, 1, 1);
        vertices[2] = new Vector3(1, -1, 1);
        vertices[3] = new Vector3(1, 1, 1);

        mesh.vertices = vertices;

        int[] indices = new int[6];

        indices[0] = 0;
        indices[1] = 1;
        indices[2] = 2;

        indices[3] = 2;
        indices[4] = 1;
        indices[5] = 3;

        mesh.triangles = indices;
        mesh.RecalculateBounds();

        return mesh;
    }
}
