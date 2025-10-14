using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ScreenSpaceOutline
{
    public class ScreenSpaceOutline : ScriptableRendererFeature
    {
        [System.Serializable]
        public class OutlineSettings
        {
            [Header("Outline Properties")]
            [Range(1f, 10f)]
            public float outlineThickness = 2f;
            
            public Color outlineColor = Color.white;
            
            [Header("Performance")]
            [Range(0.25f, 1f)]
            public float renderTextureScale = 0.5f;
            
            [Header("Render Settings")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            
            public LayerMask outlineLayerMask = -1;
        }
        
        public OutlineSettings settings = new();
        
        private ScreenSpaceOutlinePass m_OutlinePass;
        private Material m_MaskMaterial;
        private Material m_OutlineMaterial;
        
        public override void Create()
        {
            // cache shader for masking (silhouette rendering)
            var maskShader = Shader.Find("Hidden/Outline/Mask");
            if (maskShader != null)
                m_MaskMaterial = CoreUtils.CreateEngineMaterial(maskShader);
            
            // cache shader for edge detection and actual outline drawing
            var outlineShader = Shader.Find("Hidden/Outline/Outline");
            if (outlineShader != null)
                m_OutlineMaterial = CoreUtils.CreateEngineMaterial(outlineShader);
            
            m_OutlinePass = new ScreenSpaceOutlinePass(settings, m_MaskMaterial, m_OutlineMaterial)
            {
                renderPassEvent = settings.renderPassEvent
            };
        }
        
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (m_MaskMaterial == null || m_OutlineMaterial == null)
            {
                Debug.LogWarning("Outline materials are missing. Please ensure shaders are in the project.");
                return;
            }
            
            if (renderingData.cameraData.cameraType == CameraType.Game || 
                renderingData.cameraData.cameraType == CameraType.SceneView)
            {
                renderer.EnqueuePass(m_OutlinePass);
            }
        }
        
        protected override void Dispose(bool disposing)
        {
            if (!disposing)
                return;
            
            CoreUtils.Destroy(m_MaskMaterial);
            CoreUtils.Destroy(m_OutlineMaterial);
            m_OutlinePass?.Dispose();
        }
    }
}