using System.Collections;
using Unity.XR.CompositionLayers.Extensions;
using UnityEngine;

namespace ImmersiveVideo
{
    // Animates only the alpha channel of a ColorScaleBiasExtension.Scale to cross-fade
    // an Equirect composition layer over passthrough at the compositor level. RGB stays
    // at 1,1,1 (scaling color would darken the video instead of fading it), and Bias is
    // left untouched at (0,0,0,0).
    public class CompositionLayerFader : MonoBehaviour
    {
        [SerializeField] ColorScaleBiasExtension m_ColorScaleBias;

        public float Alpha
        {
            get => m_ColorScaleBias != null ? m_ColorScaleBias.Scale.w : 0f;
            set => SetImmediate(value);
        }

        void Awake()
        {
            if (m_ColorScaleBias == null)
                m_ColorScaleBias = GetComponent<ColorScaleBiasExtension>();
            if (m_ColorScaleBias == null)
                Debug.LogError("[ImmersiveVideo] CompositionLayerFader requires a ColorScaleBiasExtension " +
                    "on the same GameObject (or assigned in the Inspector).", this);
        }

        public void SetImmediate(float alpha)
        {
            if (m_ColorScaleBias == null)
                return;
            m_ColorScaleBias.Scale = new Vector4(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        public IEnumerator FadeTo(float target, float seconds, AnimationCurve curve)
        {
            if (m_ColorScaleBias == null)
                yield break;

            if (seconds <= 0f)
            {
                SetImmediate(target);
                yield break;
            }

            var start = m_ColorScaleBias.Scale.w;
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = curve != null ? curve.Evaluate(elapsed / seconds) : elapsed / seconds;
                SetImmediate(Mathf.LerpUnclamped(start, target, t));
                yield return null;
            }
            SetImmediate(target);
        }
    }
}
