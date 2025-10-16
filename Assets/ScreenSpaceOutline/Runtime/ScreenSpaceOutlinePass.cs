using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ScreenSpaceOutline
{
    public class ScreenSpaceOutlinePass : ScriptableRenderPass
    {
        private static readonly int OutlineThicknessID = Shader.PropertyToID("_OutlineThickness");
        private static readonly int OutlineColorID = Shader.PropertyToID("_OutlineColor");
        private static readonly int MaskTextureID = Shader.PropertyToID("_MaskTexture");
        private static readonly int SourceTextureID = Shader.PropertyToID("_SourceTexture");
        private static readonly int MaskTexelSizeID = Shader.PropertyToID("_MaskTexture_TexelSize");

        private static readonly ProfilingSampler CopyColorProfile = new("Outline_CopyColor");
        
        private readonly ScreenSpaceOutline.OutlineSettings m_Settings;
        private readonly Material m_MaskMaterial;
        private readonly Material m_OutlineMaterial;
        private readonly MaterialPropertyBlock m_MaskMaterialPropertyBlock;

        private RTHandle m_SourceCopy;
        private RTHandle m_MaskTexture;
        private RTHandle m_ResolvedMaskTexture;
        private RTHandle m_TempTexture;
        private FilteringSettings m_FilteringSettings;
        
        public ScreenSpaceOutlinePass(ScreenSpaceOutline.OutlineSettings settings, Material maskMat, Material outlineMat)
        {
            m_Settings = settings;
            m_MaskMaterial = maskMat;
            m_OutlineMaterial = outlineMat;
            m_MaskMaterialPropertyBlock = new MaterialPropertyBlock();
            
            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, settings.outlineLayerMask);
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            
            // set to mask resolution to current resolution
            var maskWidth = descriptor.width;
            var maskHeight = descriptor.height;
            
            // if outline thickness is small enough, reduce the resolution
            if (m_Settings.outlineThickness <= 2f)
            {
                maskWidth = Mathf.RoundToInt(descriptor.width * m_Settings.renderTextureScale);
                maskHeight = Mathf.RoundToInt(descriptor.height * m_Settings.renderTextureScale);
            }
            
            var maskDescriptor = descriptor;
            maskDescriptor.width = maskWidth;
            maskDescriptor.height = maskHeight;
            maskDescriptor.msaaSamples = m_Settings.useMSAA ? 4 : 1;
            
            RenderingUtils.ReAllocateIfNeeded(ref m_MaskTexture, maskDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineMask");

            if (m_Settings.useMSAA)
            {
                var resolvedDescriptor = maskDescriptor;
                resolvedDescriptor.msaaSamples = 1;
                RenderingUtils.ReAllocateIfNeeded(ref m_ResolvedMaskTexture, resolvedDescriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineMaskResolved");
            }
            
            RenderingUtils.ReAllocateIfNeeded(ref m_TempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineTemp");
        }
        
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // collects outlinable objects
            var renderers = new List<OutlineRenderer>();
            OutlineRenderer.GetActiveRenderers(renderers);

            if (renderers.Count == 0)
                return;
            
            var cmd = CommandBufferPool.Get("Outline Rendering");
            try
            {
                RenderMask(cmd, renderers, context, ref renderingData);
                RenderOutline(cmd, context, ref renderingData);
                
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
            finally
            {
                CommandBufferPool.Release(cmd);
            }
        }
        
        private void RenderMask(CommandBuffer cmd, List<OutlineRenderer> renderers, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // setup mask render target
            cmd.SetRenderTarget(m_MaskTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(true, true, Color.clear);
            
            // render outline just for cached `OutlineRenderer`s
            foreach (var outlineRenderer in renderers)
            {
                var renderer = outlineRenderer.Renderer;
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if ((m_Settings.outlineLayerMask.value & (1 << renderer.gameObject.layer)) == 0)
                    continue;
                
                m_MaskMaterialPropertyBlock.Clear();
                m_MaskMaterialPropertyBlock.SetColor(OutlineColorID, outlineRenderer.OutlineColor);
                
                renderer.SetPropertyBlock(m_MaskMaterialPropertyBlock);

                var subMeshCount = 1;
                if (renderer is MeshRenderer meshRenderer)
                {
                    var filter = meshRenderer.GetComponent<MeshFilter>();
                    if (filter && filter.sharedMesh)
                        subMeshCount = filter.sharedMesh.subMeshCount;
                }
                else if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                {
                    if (skinnedMeshRenderer.sharedMesh)
                        subMeshCount = skinnedMeshRenderer.sharedMesh.subMeshCount;
                }
                
                for (var i = 0; i < subMeshCount; i++)
                    cmd.DrawRenderer(renderer, m_MaskMaterial, i, 0);
            }

            if (m_Settings.useMSAA)
                cmd.ResolveAntiAliasedSurface(m_MaskTexture.rt, m_ResolvedMaskTexture.rt);
        }
        
        private void RenderOutline(CommandBuffer cmd, ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var finalMask = m_Settings.useMSAA ? m_ResolvedMaskTexture : m_MaskTexture;
            var cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            var (texelWidth, texelHeight) = GetRenderTextureSize(finalMask, descriptor);
            
            EnsureSourceCopy(cmd, ref renderingData);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            // setup shader properties
            cmd.SetGlobalFloat(OutlineThicknessID, m_Settings.outlineThickness);
            cmd.SetGlobalTexture(MaskTextureID, finalMask.nameID);
            cmd.SetGlobalTexture(SourceTextureID, m_SourceCopy.nameID);
            cmd.SetGlobalVector(MaskTexelSizeID, new Vector4(1f / texelWidth, 1f / texelHeight, texelWidth, texelHeight));
            
            // draw outline to temporary render target
            descriptor.depthBufferBits = 0;
            RenderingUtils.ReAllocateIfNeeded(ref m_TempTexture, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineTemp");
            
            // apply outline
            cmd.SetRenderTarget(m_TempTexture, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.DrawProcedural(Matrix4x4.identity, m_OutlineMaterial, 0, MeshTopology.Triangles, 3, 1);
            
            // blit temporary to camera color target
            Blitter.BlitCameraTexture(cmd, m_TempTexture, cameraColorTarget);
            
            // cmd.SetRenderTarget(cameraColorTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            // cmd.Blit(m_TempTexture, cameraColorTarget);
        }
        
        public void Dispose()
        {
            m_MaskTexture?.Release();
            m_SourceCopy?.Release();
            m_ResolvedMaskTexture?.Release();
            m_TempTexture?.Release();
        }

        private (int width, int height) GetRenderTextureSize(in RTHandle handle, in RenderTextureDescriptor fallbackDesc)
        {
            return handle.rt != null
                ? (handle.rt.width, handle.rt.height)
                : (fallbackDesc.width, fallbackDesc.height);
        }

        private void EnsureSourceCopy(CommandBuffer cmd, ref RenderingData renderingData)
        {
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;

            RenderingUtils.ReAllocateIfNeeded(ref m_SourceCopy, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_OutlineSourceCopy");

            using (new ProfilingScope(cmd, CopyColorProfile))
            {
                Blitter.BlitCameraTexture(cmd, renderingData.cameraData.renderer.cameraColorTargetHandle, m_SourceCopy);
            }
        }
    }
}