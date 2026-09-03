using System;
using System.Collections;
using UnityEngine;

namespace ImmersiveVideo
{
    public enum ScanPhase
    {
        Idle,
        Scan,
        Materialize,
        Dissolve,
        Done
    }

    // TASK-10: drives the scan-reveal intro's timeline and writes the SceneRevealSurface
    // shader's animated parameters as GLOBALS (Shader.SetGlobalX). Every reveal surface
    // (mesh/plane/proxy) shares one material, so a global write reaches all of them at
    // once without per-material instances or per-surface bookkeeping.
    public class ScanRevealDirector : MonoBehaviour
    {
        [SerializeField] Transform m_SampleOriginTransform; // XR Origin's center-eye camera
        [SerializeField] CompositionLayerFader m_EquirectFader;

        static readonly int s_SampleOrigin = Shader.PropertyToID("_SampleOrigin");
        static readonly int s_ScanOrigin = Shader.PropertyToID("_ScanOrigin");
        static readonly int s_ScanRadius = Shader.PropertyToID("_ScanRadius");
        static readonly int s_RevealRadius = Shader.PropertyToID("_RevealRadius");
        static readonly int s_GlobalAlpha = Shader.PropertyToID("_GlobalAlpha");

        public event Action<ScanPhase> PhaseChanged;

        bool m_Running;

        void LateUpdate()
        {
            // _SampleOrigin must NOT freeze - both eyes sample the video from here every
            // frame, unlike _ScanOrigin which is frozen once at Phase 1 start.
            if (m_SampleOriginTransform != null)
                Shader.SetGlobalVector(s_SampleOrigin, m_SampleOriginTransform.position);
        }

        public void Abort()
        {
            m_Running = false;
            Shader.SetGlobalFloat(s_GlobalAlpha, 0f);
        }

        public IEnumerator RunAsync(ImmersiveVideoConfig config)
        {
            m_Running = true;

            Shader.SetGlobalFloat(s_GlobalAlpha, 1f);
            Shader.SetGlobalFloat(s_ScanRadius, 0f);
            Shader.SetGlobalFloat(s_RevealRadius, 0f);

            var scanOrigin = m_SampleOriginTransform != null ? m_SampleOriginTransform.position : Vector3.zero;
            Shader.SetGlobalVector(s_ScanOrigin, scanOrigin);

            if (config.preScanDelaySeconds > 0f)
                yield return new WaitForSecondsRealtime(config.preScanDelaySeconds);
            if (!m_Running)
                yield break;

            SetPhase(ScanPhase.Scan);
            yield return AnimateGlobalFloat(s_ScanRadius, 0f, config.scanMaxRadius, config.scanSeconds, config.scanCurve);
            if (!m_Running)
                yield break;

            SetPhase(ScanPhase.Materialize);
            yield return AnimateGlobalFloat(s_RevealRadius, 0f, config.scanMaxRadius, config.materializeSeconds, config.revealCurve);
            if (!m_Running)
                yield break;

            SetPhase(ScanPhase.Dissolve);
            // Sequential, not concurrent: the surface is still fully opaque here, hiding the
            // video layer completely, so fading the video in first is invisible - nothing to
            // synchronize, nothing that can look "disconnected". Only once the video is fully
            // in (fadeInSeconds) does the surface start dissolving away (dissolveSeconds) to
            // reveal it. Total phase length is fadeInSeconds + dissolveSeconds.
            if (m_EquirectFader != null)
                yield return m_EquirectFader.FadeTo(1f, config.fadeInSeconds, config.fadeInCurve);
            yield return AnimateGlobalFloat(s_GlobalAlpha, 1f, 0f, config.dissolveSeconds, config.fadeInCurve);

            SetPhase(ScanPhase.Done);
            m_Running = false;
        }

        IEnumerator AnimateGlobalFloat(int propertyId, float from, float to, float seconds, AnimationCurve curve)
        {
            if (seconds <= 0f)
            {
                Shader.SetGlobalFloat(propertyId, to);
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = curve != null ? curve.Evaluate(elapsed / seconds) : elapsed / seconds;
                Shader.SetGlobalFloat(propertyId, Mathf.LerpUnclamped(from, to, t));
                yield return null;
            }
            Shader.SetGlobalFloat(propertyId, to);
        }

        void SetPhase(ScanPhase phase) => PhaseChanged?.Invoke(phase);
    }
}
