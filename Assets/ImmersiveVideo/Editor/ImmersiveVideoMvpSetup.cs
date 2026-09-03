using ImmersiveVideo;
using Unity.XR.CompositionLayers.Extensions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace ImmersiveVideo.Editor
{
    // TASK-04/06/07/08 wiring: creates the Config + RenderTexture assets and hooks up
    // PanoramicVideoPlayer, CompositionLayerFader and ImmersiveVideoDirector on the
    // scene objects TASK-02 already created. Removes the TASK-03 spike wiring, since
    // both would otherwise fight over the same VideoLayer extensions.
    static class ImmersiveVideoMvpSetup
    {
        const string k_RenderTexturePath = "Assets/ImmersiveVideo/Config/VideoRT.renderTexture";
        const string k_ConfigPath = "Assets/ImmersiveVideo/Config/ImmersiveVideoConfig.asset";

        // To switch videos: change this path (that's the only edit needed - the
        // RenderTexture size below is now read from the clip itself, not hardcoded, so it
        // stays correct for whatever resolution the new file actually is).
        const string k_VideoClipPath = "Assets/Video_6K_360.mp4";

        [MenuItem("ImmersiveVideo/Setup MVP (Video + Fade)")]
        static void SetupMvp()
        {
            var videoLayer = GameObject.Find("VideoLayer");
            if (videoLayer == null)
            {
                Debug.LogError("[ImmersiveVideo] VideoLayer not found. Run 'ImmersiveVideo/Setup Scene' first.");
                return;
            }

            var texturesExt = videoLayer.GetComponent<TexturesExtension>();
            var colorScaleBias = videoLayer.GetComponent<ColorScaleBiasExtension>();
            if (texturesExt == null || colorScaleBias == null)
            {
                Debug.LogError("[ImmersiveVideo] VideoLayer is missing TexturesExtension or ColorScaleBiasExtension.");
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<VideoClip>(k_VideoClipPath);
            if (clip == null)
            {
                Debug.LogError($"[ImmersiveVideo] Could not load VideoClip at {k_VideoClipPath}.");
                return;
            }

            RemoveSpikeWiring();

            var renderTexture = GetOrCreateRenderTexture(clip);
            var config = GetOrCreateConfig();
            config.videoClip = clip;
            EditorUtility.SetDirty(config);

            if (videoLayer.GetComponent<VideoPlayer>() == null)
                Undo.AddComponent<VideoPlayer>(videoLayer);
            var audioSource = videoLayer.GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = Undo.AddComponent<AudioSource>(videoLayer);
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;

            var panoramicPlayer = videoLayer.GetComponent<PanoramicVideoPlayer>();
            if (panoramicPlayer == null)
                panoramicPlayer = Undo.AddComponent<PanoramicVideoPlayer>(videoLayer);
            SetSerializedField(panoramicPlayer, "m_TargetTexture", renderTexture);
            SetSerializedField(panoramicPlayer, "m_AudioSource", audioSource);
            SetSerializedField(panoramicPlayer, "m_Config", config);

            texturesExt.LeftTexture = renderTexture;
            EditorUtility.SetDirty(texturesExt);

            var fader = videoLayer.GetComponent<CompositionLayerFader>();
            if (fader == null)
                fader = Undo.AddComponent<CompositionLayerFader>(videoLayer);
            SetSerializedField(fader, "m_ColorScaleBias", colorScaleBias);

            // Start fully transparent (passthrough visible) until the Director fades in.
            colorScaleBias.Scale = new Vector4(1f, 1f, 1f, 0f);
            EditorUtility.SetDirty(colorScaleBias);

            var directorGo = GameObject.Find("ImmersiveVideoDirector");
            if (directorGo == null)
            {
                directorGo = new GameObject("ImmersiveVideoDirector", typeof(ImmersiveVideoDirector));
                Undo.RegisterCreatedObjectUndo(directorGo, "Create ImmersiveVideoDirector");
            }
            var director = directorGo.GetComponent<ImmersiveVideoDirector>();
            SetSerializedField(director, "m_Config", config);
            SetSerializedField(director, "m_VideoPlayer", panoramicPlayer);
            SetSerializedField(director, "m_Fader", fader);

            AssetDatabase.SaveAssets();

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ImmersiveVideo] MVP wiring complete: {clip.name} ({renderTexture.width}x{renderTexture.height}) assigned, fade + director wired up.");
        }

        static void RemoveSpikeWiring()
        {
            var spikeController = GameObject.Find("FadeSpikeController");
            if (spikeController != null)
            {
                Debug.Log("[ImmersiveVideo] Removing TASK-03 FadeSpikeController (superseded by the real Director).");
                Undo.DestroyObjectImmediate(spikeController);
            }
        }

        static RenderTexture GetOrCreateRenderTexture(VideoClip clip)
        {
            // clip.width/height is populated by Unity's own video importer from the file's
            // real track header - reading it here (rather than hardcoding numbers found by
            // manually parsing the MP4) means swapping k_VideoClipPath to a differently
            // sized video just works, no manual resolution bookkeeping needed.
            var width = (int)clip.width;
            var height = (int)clip.height;

            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(k_RenderTexturePath);
            if (existing != null)
            {
                if (existing.width != width || existing.height != height)
                {
                    Debug.Log($"[ImmersiveVideo] VideoRT was {existing.width}x{existing.height}, " +
                        $"resizing to match {clip.name} ({width}x{height}).");
                    existing.Release();
                    existing.width = width;
                    existing.height = height;
                    EditorUtility.SetDirty(existing);
                }
                return existing;
            }

            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.Default)
            {
                name = "VideoRT",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1
            };
            AssetDatabase.CreateAsset(rt, k_RenderTexturePath);
            return rt;
        }

        static ImmersiveVideoConfig GetOrCreateConfig()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ImmersiveVideoConfig>(k_ConfigPath);
            if (existing != null)
                return existing;

            var config = ScriptableObject.CreateInstance<ImmersiveVideoConfig>();
            AssetDatabase.CreateAsset(config, k_ConfigPath);
            return config;
        }

        static void SetSerializedField(UnityEngine.Object target, string fieldName, UnityEngine.Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(fieldName);
            if (prop == null)
            {
                Debug.LogError($"[ImmersiveVideo] Serialized field '{fieldName}' not found on {target.GetType().Name}.");
                return;
            }
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
    }
}
