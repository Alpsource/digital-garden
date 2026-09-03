using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ImmersiveVideo
{
    public enum PlaybackState
    {
        Booting,
        Warmup,
        Idle,
        Intro,
        Playing,
        Outro,
        Failed
    }

    // TASK-08/TASK-10/TASK-11 combined state machine. Warmup (USE_SCENE permission,
    // geometry acquisition, video prepare) now runs exactly once; after that the
    // button-gated Idle -> Intro -> Playing -> Outro -> Idle cycle repeats. The
    // underlying mechanics (PanoramicVideoPlayer, CompositionLayerFader,
    // ScanRevealDirector, SceneGeometryProvider) are unchanged from TASK-08/10 -
    // only this outer orchestration is new.
    public class ImmersiveVideoDirector : MonoBehaviour
    {
        [SerializeField] ImmersiveVideoConfig m_Config;
        [SerializeField] PanoramicVideoPlayer m_VideoPlayer;
        [SerializeField] CompositionLayerFader m_Fader;
        [SerializeField] SceneGeometryProvider m_GeometryProvider;
        [SerializeField] ScanRevealDirector m_ScanRevealDirector;
        [SerializeField] IdleController m_IdleController;
        [SerializeField] ARCameraManager m_ArCameraManager;
        [SerializeField] AudioSource m_AudioSource;
        // The equirect video layer's own transform defines which direction is "front" -
        // yawing it to match wherever the user is looking the instant Start is pressed
        // means the video always opens facing them, regardless of which way they were
        // facing when they pressed the button.
        [SerializeField] Transform m_VideoLayer;
        // Optional second video layer, purely for smoothing the videoPlayCount > 1 loop
        // transition into a video-over-video dissolve (see AdvanceToNextPass). Always drawn
        // behind m_VideoLayer (CompositionLayer.Order set once at setup time) - safe to leave
        // unassigned, everything here null-checks it and falls back to the original
        // same-layer replay-with-a-dip behavior.
        [SerializeField] PanoramicVideoPlayer m_VideoPlayerB;
        [SerializeField] CompositionLayerFader m_FaderB;
        [SerializeField] AudioSource m_AudioSourceB;
        [SerializeField] Transform m_VideoLayerB;

        public event Action<PlaybackState> StateChanged;
        public event Action<string> Error;

        public PlaybackState State { get; private set; }

        static readonly int s_VideoYawOffsetDegrees = Shader.PropertyToID("_VideoYawOffsetDegrees");

        Coroutine m_Current;
        bool m_HasPlayedOnce;
        int m_PlaysRemaining;
        bool m_PassTransitionArmed;
        float m_PassTransitionGuardUntil;
        Coroutine m_AlphaFadeRoutine;
        Coroutine m_AudioCrossfadeRoutine;

        void OnEnable()
        {
            m_VideoPlayer.Failed += OnFailed;
            m_IdleController.StartRequested += OnStartRequested;
        }

        void OnDisable()
        {
            m_VideoPlayer.Failed -= OnFailed;
            m_IdleController.StartRequested -= OnStartRequested;
        }

        void Start()
        {
            ChangeState(PlaybackState.Booting);
            m_Current = StartCoroutine(RunWarmup());
        }

        // Runs exactly once. Never re-entered on Idle -> Intro -> Playing -> Outro cycles.
        IEnumerator RunWarmup()
        {
            ChangeState(PlaybackState.Warmup);

            // Video Prepare() runs in parallel with geometry acquisition (spec 2/4).
            var prepareRoutine = StartCoroutine(m_VideoPlayer.PrepareAsync(m_Config.videoClip));

            // Best-effort, not awaited and not wired to Failed - a slow/failed prepare here
            // just means AdvanceToNextPass's crossfade falls back to its non-crossfade path
            // for that transition. Never blocks or fails the experience by itself.
            if (m_VideoPlayerB != null)
                StartCoroutine(m_VideoPlayerB.PrepareAsync(m_Config.videoClip));

            // Geometry is captured once as a static snapshot, then hidden immediately -
            // Warmup is passthrough-only, nothing scan-reveal related should be visible yet.
            yield return m_GeometryProvider.AcquireAsync(m_Config);
            m_GeometryProvider.SetSurfacesEnabled(false);
            if (State == PlaybackState.Failed)
                yield break;

            yield return prepareRoutine;
            if (State == PlaybackState.Failed)
                yield break;

            EnterIdle();
        }

        void EnterIdle()
        {
            ChangeState(PlaybackState.Idle);
            m_IdleController.EnterIdle();
        }

        void OnStartRequested()
        {
            if (State != PlaybackState.Idle)
                return;

            RecenterVideoToCurrentGaze();
            m_IdleController.ExitIdle();
            m_Current = StartCoroutine(RunIntroAndPlayback());
        }

        void RecenterVideoToCurrentGaze()
        {
            if (m_VideoLayer == null || m_ArCameraManager == null)
                return;

            var forward = m_ArCameraManager.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                return;

            var gazeYawDegrees = Vector3.SignedAngle(Vector3.forward, forward, Vector3.up);
            var gazeRotation = Quaternion.AngleAxis(gazeYawDegrees, Vector3.up);
            var rotation = gazeRotation * Quaternion.Euler(0f, m_Config.videoStartYawOffsetDegrees, 0f);
            m_VideoLayer.rotation = rotation;
            if (m_VideoLayerB != null)
                m_VideoLayerB.rotation = rotation; // same clip, same orientation - keep both layers in sync

            // Keep SceneRevealSurface's independent equirect sampling in lockstep with the
            // video layer's new rotation - see the shader's own comment on why this can't
            // just read the layer's transform directly (it has no visibility into the
            // native OpenXR Equirect layer's internal UV convention).
            var revealYaw = m_Config.sceneRevealYawCalibrationDegrees + gazeYawDegrees + m_Config.videoStartYawOffsetDegrees;
            Shader.SetGlobalFloat(s_VideoYawOffsetDegrees, revealYaw);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [Header("Debug")]
        [Tooltip("Tick this in Play Mode to trigger the same thing the Start button does, " +
            "without needing a working ray/poke interactor. Resets itself automatically.")]
        [SerializeField] bool m_DebugForceStart;

        void Update()
        {
            if (m_DebugForceStart)
            {
                m_DebugForceStart = false;
                OnStartRequested();
            }
        }
#endif

        IEnumerator RunIntroAndPlayback()
        {
            ChangeState(PlaybackState.Intro);

            if (m_Config.reacquireGeometryEachCycle && m_HasPlayedOnce)
            {
                yield return m_GeometryProvider.AcquireAsync(m_Config);
                m_GeometryProvider.SetSurfacesEnabled(false);
                if (State == PlaybackState.Failed)
                    yield break;
            }

            if (!m_Config.keepVideoPreparedWhileIdle && m_HasPlayedOnce)
            {
                // Idle's CleanUpAfterOutro called Stop() instead of Pause(), so the decoder
                // needs a fresh Prepare() before this cycle can Play().
                yield return m_VideoPlayer.PrepareAsync(m_Config.videoClip);
                if (State == PlaybackState.Failed)
                    yield break;
            }

            // ReplayIntroMode.Shortened is not implemented yet - falls back to the full
            // intro (Always), per spec instruction to stub rather than skip silently.
            var useFullIntro = m_Config.replayIntroMode != ReplayIntroMode.FirstTimeOnly || !m_HasPlayedOnce;

            if (useFullIntro && m_Config.introMode == IntroMode.ScanReveal && m_GeometryProvider.Tier != GeometryTier.None)
                yield return RunScanRevealPath();
            else if (useFullIntro)
                yield return RunSimpleFadePath();
            else
                yield return RunQuickReplayFadePath();

            if (State == PlaybackState.Failed)
                yield break;

            m_HasPlayedOnce = true;
            m_PlaysRemaining = Mathf.Max(1, m_Config.videoPlayCount);
            m_PassTransitionArmed = false;
            ChangeState(PlaybackState.Playing);
            StartCoroutine(WatchPlayback());
        }

        // Single source of truth for advancing through videoPlayCount plays and for starting
        // Outro config.outroLeadSeconds before the final play's natural end. Replaces the old
        // VideoPlayer.loopPointReached-driven approach: a pass can now be running on either
        // of two layers (see AdvanceToNextPass), so it always polls whichever layer is
        // CURRENTLY VISIBLE (IsLayerAVisible) instead of always assuming layer A.
        IEnumerator WatchPlayback()
        {
            while (State == PlaybackState.Playing)
            {
                var aVisible = IsLayerAVisible;
                var activePlayer = aVisible ? m_VideoPlayer : m_VideoPlayerB;
                if (activePlayer == null)
                {
                    yield return null;
                    continue;
                }

                var duration = activePlayer.Duration;
                var isFinalPass = m_PlaysRemaining <= 1;
                var threshold = isFinalPass ? m_Config.outroLeadSeconds : m_Config.loopCrossfadeSeconds;
                // Right after AdvanceToNextPass, m_PlaysRemaining has already dropped (so
                // isFinalPass can flip true) and/or activePlayer has already switched to the
                // just-seeked layer - but the crossfade itself (or VideoPlayer.time's
                // asynchronous seek) hasn't visually/numerically settled yet. Without this
                // guard, "duration - activePlayer.Time" can still read a tiny/stale value for
                // a frame or two and immediately re-trigger, cutting the transition short.
                var pastTransitionGuard = Time.unscaledTime >= m_PassTransitionGuardUntil;

                if (!m_PassTransitionArmed && pastTransitionGuard && duration > 0 && duration - activePlayer.Time <= threshold)
                {
                    m_PassTransitionArmed = true;

                    if (isFinalPass)
                    {
                        BeginOutro();
                        yield break;
                    }

                    AdvanceToNextPass(aVisible);
                }

                yield return null;
            }
        }

        // True whenever layer A is what the viewer currently sees (including the whole
        // single-layer/videoPlayCount<=1 case, where A's alpha is always 1 throughout
        // Playing and B is never touched). False only during/after a crossfade that
        // revealed B - see AdvanceToNextPass for how that alpha ends up at 0.
        bool IsLayerAVisible => m_Fader == null || m_Fader.Alpha >= 0.5f;

        // Starts the next of videoPlayCount plays. If the crossfade layer (B) is assigned
        // and finished preparing, the upcoming pass is loaded silently onto whichever layer
        // is currently fully hidden and revealed with a config.loopCrossfadeSeconds
        // video-over-video dissolve - passthrough is never exposed, because the hidden layer
        // is always fully opaque before it's revealed. B's own alpha, once set to 1 the
        // first time it's used, is never touched again; only layer A's alpha ever animates
        // (down to reveal B, or back up to cover B) - see the crossfade layer's own setup
        // comment for why both layers can stay at fixed CompositionLayer.Order forever.
        // Falls back to the original same-layer replay-with-a-brief-dip if B isn't ready.
        void AdvanceToNextPass(bool aCurrentlyVisible)
        {
            m_PlaysRemaining--;
            m_PassTransitionArmed = false;
            // Give the crossfade (or the fallback dip) time to actually finish - see the
            // guard comment in WatchPlayback for why this can't be re-armed immediately.
            m_PassTransitionGuardUntil = Time.unscaledTime + m_Config.loopCrossfadeSeconds;

            var crossfadeReady = m_VideoPlayerB != null && m_FaderB != null && m_VideoPlayerB.IsPrepared;

            if (crossfadeReady && aCurrentlyVisible)
            {
                // B is fully hidden behind opaque A right now - safe to load its next pass
                // silently and mark it opaque (still invisible, blocked by A).
                m_VideoPlayerB.SeekToStart();
                m_VideoPlayerB.Play();
                m_FaderB.SetImmediate(1f);
                FadeLayerA(0f);
                StartAudioCrossfade(m_AudioSource, m_AudioSourceB);
                return;
            }

            if (crossfadeReady && !aCurrentlyVisible)
            {
                // A is fully hidden (alpha 0) right now - safe to load its next pass
                // silently, then bring it back to cover B.
                m_VideoPlayer.SeekToStart();
                m_VideoPlayer.Play();
                FadeLayerA(1f);
                StartAudioCrossfade(m_AudioSourceB, m_AudioSource);
                return;
            }

            // Fallback: crossfade layer not assigned or not ready yet - same-layer replay
            // with a brief dip (this project's behavior before the crossfade layer existed).
            m_Fader.SetImmediate(0f);
            m_VideoPlayer.SeekToStart();
            m_VideoPlayer.Play();
            FadeLayerA(1f);
        }

        void FadeLayerA(float target)
        {
            if (m_AlphaFadeRoutine != null)
                StopCoroutine(m_AlphaFadeRoutine);
            m_AlphaFadeRoutine = StartCoroutine(m_Fader.FadeTo(target, m_Config.loopCrossfadeSeconds, m_Config.fadeInCurve));
        }

        void StartAudioCrossfade(AudioSource from, AudioSource to)
        {
            if (m_AudioCrossfadeRoutine != null)
                StopCoroutine(m_AudioCrossfadeRoutine);
            m_AudioCrossfadeRoutine = StartCoroutine(CrossfadeAudioVolumes(from, to, m_Config.loopCrossfadeSeconds));
        }

        // Mirrors FadeLayerA's animation for audio: unlike video, sound can't be hidden by
        // draw order, so both volumes actually need to move (one down, one up) or the
        // incoming layer's audio would briefly double up with the outgoing one's.
        IEnumerator CrossfadeAudioVolumes(AudioSource from, AudioSource to, float seconds)
        {
            if (to != null)
                to.volume = 0f;
            var fromStart = from != null ? from.volume : 0f;

            if (seconds <= 0f)
            {
                if (from != null) from.volume = 0f;
                if (to != null) to.volume = 1f;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = m_Config.fadeInCurve != null ? m_Config.fadeInCurve.Evaluate(elapsed / seconds) : elapsed / seconds;
                if (from != null) from.volume = Mathf.LerpUnclamped(fromStart, 0f, t);
                if (to != null) to.volume = Mathf.LerpUnclamped(0f, 1f, t);
                yield return null;
            }
            if (from != null) from.volume = 0f;
            if (to != null) to.volume = 1f;
        }

        IEnumerator RunSimpleFadePath()
        {
            yield return new WaitForSecondsRealtime(m_Config.passthroughHoldSeconds);
            m_VideoPlayer.Play();
            yield return m_Fader.FadeTo(1f, m_Config.fadeInSeconds, m_Config.fadeInCurve);
        }

        // ReplayIntroMode.FirstTimeOnly, for every play after the first: no hold, no scan.
        IEnumerator RunQuickReplayFadePath()
        {
            const float quickFadeSeconds = 1.5f;
            m_VideoPlayer.Play();
            yield return m_Fader.FadeTo(1f, quickFadeSeconds, m_Config.fadeInCurve);
        }

        IEnumerator RunScanRevealPath()
        {
            m_GeometryProvider.SetSurfacesEnabled(true);

            void OnScanPhaseChanged(ScanPhase phase)
            {
                if (phase == ScanPhase.Materialize)
                    m_VideoPlayer.Play(); // alpha is still 0 here - layer stays invisible
            }

            m_ScanRevealDirector.PhaseChanged += OnScanPhaseChanged;
            yield return m_ScanRevealDirector.RunAsync(m_Config);
            m_ScanRevealDirector.PhaseChanged -= OnScanPhaseChanged;

            m_GeometryProvider.SetSurfacesEnabled(false);
        }

        void OnFailed(string reason)
        {
            if (m_Current != null)
                StopCoroutine(m_Current);
            m_ScanRevealDirector.Abort();
            ChangeState(PlaybackState.Failed);
            Debug.LogError($"[ImmersiveVideo] Video failed: {reason}");
            Error?.Invoke(reason);
        }

        // No error-panel UI yet (deferred, same as the main spec's AC-8) - this is the
        // hook a future "Retry" button would call.
        public void RetryAfterFailure()
        {
            if (State != PlaybackState.Failed)
                return;
            m_Current = StartCoroutine(RunWarmup());
        }

        // No input binding wired up for this yet (long-press B/Y or a hand gesture, per
        // spec section 7 "optional, note it if skipped") - exposed as a public hook.
        public void RequestEarlyExit()
        {
            if (State != PlaybackState.Playing || !m_Config.allowEarlyExit)
                return;
            BeginOutro();
        }

        void BeginOutro()
        {
            if (m_Current != null)
                StopCoroutine(m_Current);
            // In case Outro starts mid-crossfade (e.g. RequestEarlyExit lands during a loop
            // transition) - stop those so they can't fight RunOutroSimpleFade over alpha.
            if (m_AlphaFadeRoutine != null)
                StopCoroutine(m_AlphaFadeRoutine);
            if (m_AudioCrossfadeRoutine != null)
                StopCoroutine(m_AudioCrossfadeRoutine);
            ChangeState(PlaybackState.Outro);
            m_Current = StartCoroutine(RunOutro());
        }

        IEnumerator RunOutro()
        {
            // 5.1: confirm passthrough is actually producing frames before revealing it -
            // skipping this can show a black gap between the video and the real world.
            yield return WaitForPassthroughWarm();

            switch (m_Config.outroMode)
            {
                case OutroMode.ReverseScan:
                    // Not implemented yet - falls back to SimpleFade. TODO: symmetric
                    // reveal-in-reverse per spec 5.2.
                case OutroMode.SimpleFade:
                default:
                    yield return RunOutroSimpleFade();
                    break;
            }

            CleanUpAfterOutro();
            EnterIdle();
        }

        IEnumerator WaitForPassthroughWarm()
        {
            if (m_ArCameraManager == null)
                yield break;

            m_ArCameraManager.enabled = true;

            var frameReceived = false;
            void OnFrame(ARCameraFrameEventArgs args) => frameReceived = true;
            m_ArCameraManager.frameReceived += OnFrame;

            var elapsed = 0f;
            while (!frameReceived && elapsed < m_Config.passthroughWarmupSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            m_ArCameraManager.frameReceived -= OnFrame;
        }

        IEnumerator RunOutroSimpleFade()
        {
            // videoPlayCount <= 1 (or a crossfade that never actually revealed B) means A is
            // still what's visible here, same as before B existed. A crossfade that DID
            // reveal B partway through Playing means B is what's actually on screen now -
            // fade that one instead, or the video would just sit at full brightness forever.
            var aVisible = IsLayerAVisible;
            var activeFader = aVisible ? m_Fader : m_FaderB;
            var activeAudio = aVisible ? m_AudioSource : m_AudioSourceB;

            var startAlpha = activeFader.Alpha;
            var startVolume = activeAudio != null ? activeAudio.volume : 1f;
            var duration = m_Config.outroSeconds;
            var elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var t = m_Config.outroCurve != null ? m_Config.outroCurve.Evaluate(elapsed / duration) : elapsed / duration;
                // Video and audio share the exact same curve/progress - a mismatch here
                // reads as the picture and sound disconnecting from each other (spec 5.3).
                activeFader.SetImmediate(Mathf.LerpUnclamped(startAlpha, 0f, t));
                if (activeAudio != null)
                    activeAudio.volume = Mathf.LerpUnclamped(startVolume, 0f, t);
                yield return null;
            }

            activeFader.SetImmediate(0f);
            if (activeAudio != null)
                activeAudio.volume = 0f;
        }

        void CleanUpAfterOutro()
        {
            if (m_Config.keepVideoPreparedWhileIdle)
            {
                // Deliberately Pause(), not Stop() - Stop() tears down the decoder and
                // would require a fresh Prepare() next time.
                m_VideoPlayer.Pause();
                m_VideoPlayer.SeekToStart();
            }
            else
            {
                m_VideoPlayer.Stop();
            }

            if (m_AudioSource != null)
                m_AudioSource.volume = 1f;

            // A crossfade may have left B visible/mid-transition (or A momentarily hidden) -
            // reset both layers to the same clean, fully-hidden baseline every cycle ends
            // with, so the next Idle -> Intro always starts from the same known state that
            // RunSimpleFadePath/RunQuickReplayFadePath/RunScanRevealPath (all layer-A-only,
            // unchanged) already assume.
            m_Fader.SetImmediate(0f);
            if (m_FaderB != null)
                m_FaderB.SetImmediate(0f);
            if (m_VideoPlayerB != null)
            {
                if (m_Config.keepVideoPreparedWhileIdle)
                {
                    m_VideoPlayerB.Pause();
                    m_VideoPlayerB.SeekToStart();
                }
                else
                {
                    m_VideoPlayerB.Stop();
                }
            }
            if (m_AudioSourceB != null)
                m_AudioSourceB.volume = 1f;

            m_GeometryProvider.SetSurfacesEnabled(false);
        }

        void ChangeState(PlaybackState state)
        {
            State = state;
            if (m_Config.verboseLogging)
                Debug.Log($"[ImmersiveVideo] State -> {state}");
            StateChanged?.Invoke(state);
        }
    }
}
