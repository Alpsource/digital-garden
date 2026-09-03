using System.Collections;
using Unity.XR.CompositionLayers.Extensions;
using UnityEngine;

namespace ImmersiveVideo.Spike
{
    // TASK-03 throwaway spike: holds passthrough for a few seconds, then fades the
    // Equirect layer's Color Scale/Bias alpha 0->1 to prove the compositor-level
    // cross-fade actually works on-device. Delete once the architecture is verified.
    public class FadeSpike : MonoBehaviour
    {
        [SerializeField] ColorScaleBiasExtension m_ColorScaleBias;
        [SerializeField] float m_HoldSeconds = 5f;
        [SerializeField] float m_FadeSeconds = 3f;

        IEnumerator Start()
        {
            m_ColorScaleBias.Scale = new Vector4(1f, 1f, 1f, 0f);
            yield return new WaitForSecondsRealtime(m_HoldSeconds);

            var elapsed = 0f;
            while (elapsed < m_FadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var alpha = Mathf.Clamp01(elapsed / m_FadeSeconds);
                m_ColorScaleBias.Scale = new Vector4(1f, 1f, 1f, alpha);
                yield return null;
            }
            m_ColorScaleBias.Scale = new Vector4(1f, 1f, 1f, 1f);
        }
    }
}
