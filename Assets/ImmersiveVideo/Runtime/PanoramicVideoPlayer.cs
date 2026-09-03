using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Video;

namespace ImmersiveVideo
{
    // Thin wrapper around UnityEngine.Video.VideoPlayer. Knows nothing about composition
    // layers or fading - just prepares and plays a clip into a RenderTexture.
    [RequireComponent(typeof(VideoPlayer))]
    public class PanoramicVideoPlayer : MonoBehaviour
    {
        [SerializeField] RenderTexture m_TargetTexture;
        [SerializeField] AudioSource m_AudioSource;
        [SerializeField] ImmersiveVideoConfig m_Config;

        VideoPlayer m_VideoPlayer;

        public event Action Prepared;
        public event Action<string> Failed;
        public event Action ReachedEnd;

        public bool IsPrepared { get; private set; }
        public double Time => m_VideoPlayer.time;
        public double Duration => m_VideoPlayer.length;

        void Awake()
        {
            m_VideoPlayer = GetComponent<VideoPlayer>();
            m_VideoPlayer.source = VideoSource.VideoClip;
            m_VideoPlayer.renderMode = VideoRenderMode.RenderTexture;
            m_VideoPlayer.targetTexture = m_TargetTexture;
            m_VideoPlayer.playOnAwake = false;
            m_VideoPlayer.waitForFirstFrame = true;
            m_VideoPlayer.skipOnDrop = true;
            // TASK-11: the experience is now button-started/Outro-ended, never auto-looping.
            m_VideoPlayer.isLooping = false;
            m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            m_VideoPlayer.EnableAudioTrack(0, true);
            m_VideoPlayer.SetTargetAudioSource(0, m_AudioSource);
            m_VideoPlayer.errorReceived += OnErrorReceived;
            m_VideoPlayer.loopPointReached += OnLoopPointReached;
        }

        public IEnumerator PrepareAsync(VideoClip clip)
        {
            IsPrepared = false;
            ResizeTargetTextureToMatch(clip);
            ClearTargetTexture();

            m_VideoPlayer.clip = clip;
            m_VideoPlayer.Prepare();

            var elapsed = 0f;
            while (!m_VideoPlayer.isPrepared)
            {
                elapsed += UnityEngine.Time.unscaledDeltaTime;
                if (elapsed > m_Config.prepareTimeoutSeconds)
                {
                    Failed?.Invoke($"Video prepare timed out after {m_Config.prepareTimeoutSeconds}s");
                    yield break;
                }
                yield return null;
            }

            // Decode and hold the first frame before anything fades in, so the layer never
            // shows a black/stale frame the instant it becomes visible.
            m_VideoPlayer.frame = 0;
            m_VideoPlayer.Play();
            m_VideoPlayer.Pause();

            IsPrepared = true;
            Prepared?.Invoke();
        }

        // Lets swapping the video be a pure "drag a new clip onto ImmersiveVideoConfig's
        // Video Clip field" operation - no Editor menu command, no code change. The
        // RenderTexture is the same object the VideoLayer's TexturesExtension already
        // references, so resizing it in place (rather than replacing it) needs no rewiring.
        void ResizeTargetTextureToMatch(VideoClip clip)
        {
            if (m_TargetTexture == null || clip == null)
                return;

            var width = (int)clip.width;
            var height = (int)clip.height;
            if (width <= 0 || height <= 0 || (m_TargetTexture.width == width && m_TargetTexture.height == height))
                return;

            if (m_Config.verboseLogging)
                Debug.Log($"[ImmersiveVideo] Resizing VideoRT from {m_TargetTexture.width}x{m_TargetTexture.height} " +
                    $"to {width}x{height} to match {clip.name}.");

            m_TargetTexture.Release();
            m_TargetTexture.width = width;
            m_TargetTexture.height = height;
        }

        void ClearTargetTexture()
        {
            if (m_TargetTexture == null)
                return;
            var previous = RenderTexture.active;
            RenderTexture.active = m_TargetTexture;
            GL.Clear(true, true, Color.black);
            RenderTexture.active = previous;
        }

        public void Play() => m_VideoPlayer.Play();
        public void Pause() => m_VideoPlayer.Pause();
        public void Stop() => m_VideoPlayer.Stop();
        public void SeekToStart() => m_VideoPlayer.time = 0;

        void OnErrorReceived(VideoPlayer source, string message) => Failed?.Invoke(message);
        void OnLoopPointReached(VideoPlayer source) => ReachedEnd?.Invoke();
    }
}
