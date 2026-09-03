using ImmersiveVideo;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace ImmersiveVideo.Editor
{
    // Adds a second, purely-cosmetic equirect video layer ("VideoLayerB") used only to
    // smooth the videoPlayCount > 1 loop transition into a video-over-video dissolve
    // instead of a same-layer restart. Mirrors ImmersiveVideoSceneSetup/ImmersiveVideoMvpSetup's
    // own patterns for the existing VideoLayer exactly, so it behaves like a second copy of
    // an already-proven setup rather than new, unverified wiring.
    //
    // Deliberately does not touch anything under StartButtonCanvas/StartButton (the button,
    // its TMP labels) or any TASK-11/FIX-PLAN objects - this only creates VideoLayerB and
    // wires the new *B fields on the existing ImmersiveVideoDirector.
    static class ImmersiveVideoCrossfadeSetup
    {
        const string k_VideoLayerBName = "VideoLayerB";
        const string k_RenderTextureBPath = "Assets/ImmersiveVideo/Config/VideoRT_B.renderTexture";

        [MenuItem("ImmersiveVideo/Setup Loop Crossfade (Layer B)")]
        static void SetupCrossfade()
        {
            var videoLayer = GameObject.Find("VideoLayer");
            var directorGo = GameObject.Find("ImmersiveVideoDirector");
            var director = directorGo != null ? directorGo.GetComponent<ImmersiveVideoDirector>() : null;
            if (videoLayer == null || director == null)
            {
                Debug.LogError("[ImmersiveVideo] VideoLayer/ImmersiveVideoDirector not found. Run 'ImmersiveVideo/Setup MVP (Video + Fade)' first.");
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<ImmersiveVideoConfig>("Assets/ImmersiveVideo/Config/ImmersiveVideoConfig.asset");
            if (config == null || config.videoClip == null)
            {
                Debug.LogError("[ImmersiveVideo] ImmersiveVideoConfig.asset (with a videoClip assigned) not found.");
                return;
            }

            var videoLayerB = GetOrCreateVideoLayerB(videoLayer.transform.parent);
            if (videoLayerB == null)
                return;

            var renderTextureB = GetOrCreateRenderTextureB(config.videoClip);

            var texturesExt = videoLayerB.GetComponent<TexturesExtension>();
            if (texturesExt == null)
            {
                Debug.LogError("[ImmersiveVideo] VideoLayerB is missing TexturesExtension - the " +
                    "'Equirect Layer' menu command should have added it automatically.");
                return;
            }
            texturesExt.LeftTexture = renderTextureB;
            EditorUtility.SetDirty(texturesExt);

            var colorScaleBias = videoLayerB.GetComponent<ColorScaleBiasExtension>();
            if (colorScaleBias == null)
                colorScaleBias = Undo.AddComponent<ColorScaleBiasExtension>(videoLayerB);
            colorScaleBias.Bias = Vector4.zero;
            // Unconditional, matching VideoLayer's own setup exactly - a freshly-added
            // ColorScaleBiasExtension does NOT default its Scale to zero-alpha (it was
            // showing fully opaque, i.e. "the video is visible before Start is even
            // pressed", until this was made unconditional), so a "only if still default"
            // guard here does nothing useful and leaves it visible instead of hidden.
            colorScaleBias.Scale = new Vector4(1f, 1f, 1f, 0f);

            if (videoLayerB.GetComponent<VideoPlayer>() == null)
                Undo.AddComponent<VideoPlayer>(videoLayerB);

            var audioSourceB = videoLayerB.GetComponent<AudioSource>();
            if (audioSourceB == null)
                audioSourceB = Undo.AddComponent<AudioSource>(videoLayerB);
            audioSourceB.playOnAwake = false;
            audioSourceB.spatialBlend = 0f;

            var panoramicPlayerB = videoLayerB.GetComponent<PanoramicVideoPlayer>();
            if (panoramicPlayerB == null)
                panoramicPlayerB = Undo.AddComponent<PanoramicVideoPlayer>(videoLayerB);
            SetSerializedField(panoramicPlayerB, "m_TargetTexture", renderTextureB);
            SetSerializedField(panoramicPlayerB, "m_AudioSource", audioSourceB);
            SetSerializedField(panoramicPlayerB, "m_Config", config);

            var faderB = videoLayerB.GetComponent<CompositionLayerFader>();
            if (faderB == null)
                faderB = Undo.AddComponent<CompositionLayerFader>(videoLayerB);
            SetSerializedField(faderB, "m_ColorScaleBias", colorScaleBias);

            SetSerializedField(director, "m_VideoPlayerB", panoramicPlayerB);
            SetSerializedField(director, "m_FaderB", faderB);
            SetSerializedField(director, "m_AudioSourceB", audioSourceB);
            SetSerializedField(director, "m_VideoLayerB", videoLayerB.transform);

            AssetDatabase.SaveAssets();

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ImmersiveVideo] Loop crossfade wiring complete: VideoLayerB ({renderTextureB.width}x{renderTextureB.height}) " +
                "created behind VideoLayer and wired to the Director. videoPlayCount <= 1 never uses it - no behavior change for that case.");
        }

        static GameObject GetOrCreateVideoLayerB(Transform parent)
        {
            var go = GameObject.Find(k_VideoLayerBName);
            if (go == null)
            {
                Selection.activeGameObject = null;
                EditorApplication.ExecuteMenuItem("GameObject/XR/Composition Layers/Equirect Layer");
                go = Selection.activeGameObject;
                if (go == null)
                {
                    Debug.LogError("[ImmersiveVideo] 'GameObject/XR/Composition Layers/Equirect Layer' did not create an object as expected.");
                    return null;
                }
                go.name = k_VideoLayerBName;
            }

            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // CompositionLayer.Order is an int - there's no integer between Passthrough's -2
            // and VideoLayer's -1 to slot this into. Push Passthrough back to -3 instead
            // (only its position relative to the video layers matters, not the absolute
            // number) so -2 is free, so the order stays Passthrough(-3) < VideoLayerB(-2) <
            // VideoLayer(-1) - always drawn behind VideoLayer, only visible when VideoLayer's
            // own alpha lets it show through.
            //
            // MUST happen before setting VideoLayerB's own Order below: assigning an Order
            // that's still in use gets silently redirected to "the first available POSITIVE
            // slot instead" (this package's own change-order.md) - that's what put VideoLayerB
            // at Order 2, in FRONT of everything, showing the video immediately even in Idle.
            var passthroughLayer = GameObject.Find("PassthroughLayer");
            var passthroughComponent = passthroughLayer != null ? passthroughLayer.GetComponent<CompositionLayer>() : null;
            if (passthroughComponent != null)
                passthroughComponent.Order = -3;
            else
                Debug.LogWarning("[ImmersiveVideo] PassthroughLayer not found - could not push it back to make room for VideoLayerB's order.");

            var layer = go.GetComponent<CompositionLayer>();
            if (layer != null)
            {
                layer.Order = -2;

                if (layer.LayerData is EquirectMeshLayerData equirect)
                {
                    equirect.Radius = 100f;
                    equirect.CentralHorizontalAngleInDegrees = 360.1f; // matches VideoLayer, see its own setup comment
                }
            }

            return go;
        }

        static RenderTexture GetOrCreateRenderTextureB(VideoClip clip)
        {
            var width = (int)clip.width;
            var height = (int)clip.height;

            var existing = AssetDatabase.LoadAssetAtPath<RenderTexture>(k_RenderTextureBPath);
            if (existing != null)
            {
                if (existing.width != width || existing.height != height)
                {
                    existing.Release();
                    existing.width = width;
                    existing.height = height;
                    EditorUtility.SetDirty(existing);
                }
                return existing;
            }

            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.Default)
            {
                name = "VideoRT_B",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                antiAliasing = 1
            };
            AssetDatabase.CreateAsset(rt, k_RenderTextureBPath);
            return rt;
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
