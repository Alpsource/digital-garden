using UnityEngine;
using UnityEngine.Video;

namespace ImmersiveVideo
{
    // TASK-10 addendum. SimpleFade is the original, already-working path and must
    // keep behaving exactly as before - ScanReveal is purely additive.
    public enum IntroMode
    {
        SimpleFade,
        ScanReveal
    }

    // TASK-11 addendum.
    public enum OutroMode
    {
        SimpleFade,
        ReverseScan // not implemented yet - falls back to SimpleFade, see ImmersiveVideoDirector
    }

    public enum ReplayIntroMode
    {
        Always,
        FirstTimeOnly,
        Shortened // not implemented yet - falls back to Always, see ImmersiveVideoDirector
    }

    [CreateAssetMenu(menuName = "ImmersiveVideo/Config", fileName = "ImmersiveVideoConfig")]
    public class ImmersiveVideoConfig : ScriptableObject
    {
        [Header("Video")]
        public VideoClip videoClip;

        [Header("Timing")]
        public float passthroughHoldSeconds = 5f;
        public float fadeInSeconds = 3f;
        public AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public float fadeOutSeconds = 2f;
        public AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public float prepareTimeoutSeconds = 30f;

        [Header("Diagnostics")]
        public bool verboseLogging = false;

        [Header("Intro")]
        // Deliberately defaults to SimpleFade rather than the spec's suggested
        // ScanReveal default: this field is new, and the existing config asset
        // already in use must not silently switch to the unfinished path the
        // moment this field is added. Switch it on explicitly once ready to test.
        public IntroMode introMode = IntroMode.SimpleFade;

        [Header("Scan Reveal - timing")]
        public float preScanDelaySeconds = 1.5f;
        public float scanSeconds = 2.5f;
        public float materializeSeconds = 2.0f;
        public float dissolveSeconds = 2.5f;
        public float crossfadeOverlapSeconds = 1.0f;

        [Header("Scan Reveal - wave")]
        public float scanMaxRadius = 8f;
        public AnimationCurve scanCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public AnimationCurve revealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Scan Reveal - appearance")]
        public Color gridColor = new Color(0.6f, 0.85f, 1f, 1f);
        public float gridScale = 4f;
        public float gridWidth = 0.02f;
        public float scanBandWidth = 0.6f;
        public float revealBandWidth = 1.5f;
        // Fixed per-device calibration: how far SceneRevealSurface's own manual atan2/asin
        // equirect mapping is rotated from the native OpenXR Equirect layer's internal UV
        // convention (tuned by eye in-headset, see the shader's own comment). Combined at
        // runtime with the video's start-yaw so the reveal surface's video sampling always
        // matches the actual video layer's orientation instead of a stale fixed angle.
        public float sceneRevealYawCalibrationDegrees = -99f;

        [Header("Scan Reveal - geometry")]
        public float sceneDataTimeoutSeconds = 2.5f;
        public int minPlanesForTierB = 3;
        public bool useBoundingBoxes = true;
        public bool allowProxyRoomFallback = true;
        public Vector3 proxyRoomSize = new Vector3(4f, 2.6f, 4f);

        [Header("Idle / Button")]
        public float buttonDistance = 0.55f;
        public float buttonHeightOffsetDeg = -15f;
        public float buttonDiameter = 0.18f;
        public float buttonRecenterAngle = 35f;
        public float buttonRecenterDelay = 1.5f;
        public float buttonRecenterSmooth = 0.4f;
        public bool keepVideoPreparedWhileIdle = true;
        // Applied on top of the user's gaze yaw when Start is pressed - 0 means the video's
        // front faces exactly where they're looking; positive rotates it further clockwise
        // (viewed from above), negative counter-clockwise.
        public float videoStartYawOffsetDegrees;

        [Header("Outro")]
        public OutroMode outroMode = OutroMode.SimpleFade;
        public float outroSeconds = 2.5f;
        public AnimationCurve outroCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public float passthroughWarmupSeconds = 0.35f;
        // Outro now starts this many seconds before the video's natural end, instead of
        // only once it's already finished - so passthrough is back well before the video
        // stops rather than after a dead/frozen last frame.
        public float outroLeadSeconds = 5f;
        // How many times the video plays back-to-back (audio included each time) before
        // Outro starts - 1 keeps the original single-play behavior. Not a seamless native
        // loop: each repeat is a plain seek-to-start + Play(), same as a manual replay.
        public int videoPlayCount = 1;
        // Duration of the videoPlayCount > 1 loop transition. If a second video layer is
        // wired up (ImmersiveVideoDirector's *B fields), this is a true video-over-video
        // dissolve - passthrough is never exposed. Otherwise it's a same-layer dip: fade out,
        // seek+replay while invisible, fade back in. Reuses fadeInCurve for both directions.
        public float loopCrossfadeSeconds = 3f;

        [Header("Replay")]
        public ReplayIntroMode replayIntroMode = ReplayIntroMode.Always;
        public bool reacquireGeometryEachCycle = false;

        [Header("Early exit during playback")]
        public bool allowEarlyExit = true;
        public float earlyExitHoldSeconds = 0.8f;
    }
}
