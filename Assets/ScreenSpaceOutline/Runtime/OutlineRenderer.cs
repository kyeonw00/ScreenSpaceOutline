using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScreenSpaceOutline
{
    [AddComponentMenu("Screen Space Outline/OutlineRenderer")]
    [RequireComponent(typeof(Renderer))]
    [ExecuteAlways]
    public class OutlineRenderer : MonoBehaviour
    {
        private static readonly List<OutlineRenderer> ActiveRenderers = new(32);

        [SerializeField]
        private Color outlineColor = Color.white;
        
        private Renderer m_CachedRenderer;

        public Renderer Renderer => m_CachedRenderer;

        public Color OutlineColor => outlineColor;

        public bool IsActiveAndEnabled()
        {
            return isActiveAndEnabled && m_CachedRenderer != null && m_CachedRenderer.enabled;
        }
        
        private void OnEnable()
        {
            m_CachedRenderer = GetComponent<Renderer>();

            if (ActiveRenderers.Contains(this))
                return;
            
            if (ActiveRenderers.Count > 32)
                Debug.LogWarning($"Ideal maximum outline renderer count (32) reached.");
            
            ActiveRenderers.Add(this);
        }
        
        private void OnDisable()
        {
            ActiveRenderers.Remove(this);
        }
        
        private void OnDestroy()
        {
            ActiveRenderers.Remove(this);
        }

        private void OnValidate()
        {
            m_CachedRenderer = GetComponent<Renderer>();
        }

        public static void GetActiveRenderers(List<OutlineRenderer> renderers)
        {
            renderers.Clear();
            foreach (var renderer in ActiveRenderers)
            {
                if (renderer == null || !renderer.IsActiveAndEnabled())
                    continue;
                
                renderers.Add(renderer);
            }
        }
    }
}