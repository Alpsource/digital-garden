using ImmersiveVideo;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ImmersiveVideo.Editor
{
    // TASK-10 wiring: creates the shared SceneRevealSurface material and the
    // SceneGeometryProvider / ScanRevealDirector objects, and wires them into the
    // existing ImmersiveVideoDirector. Does NOT enable ScanReveal mode - config.introMode
    // is left at SimpleFade (the working default) so nothing about the current behavior
    // changes until it's switched on deliberately in the Inspector.
    static class ImmersiveVideoTask10Setup
    {
        const string k_MaterialPath = "Assets/ImmersiveVideo/Shaders/SceneRevealSurface.mat";
        const string k_ShaderName = "ImmersiveVideo/SceneRevealSurface";

        [MenuItem("ImmersiveVideo/Setup TASK-10 (Scan Reveal)")]
        static void SetupTask10()
        {
            var xrOriginGo = GameObject.Find("XR Origin (VR)");
            var xrOrigin = xrOriginGo != null ? xrOriginGo.GetComponent<XROrigin>() : null;
            if (xrOrigin == null)
            {
                Debug.LogError("[ImmersiveVideo] XR Origin (VR) not found. Run 'ImmersiveVideo/Setup Scene' first.");
                return;
            }

            var videoLayer = GameObject.Find("VideoLayer");
            var fader = videoLayer != null ? videoLayer.GetComponent<CompositionLayerFader>() : null;
            if (fader == null)
            {
                Debug.LogError("[ImmersiveVideo] VideoLayer/CompositionLayerFader not found. Run 'ImmersiveVideo/Setup MVP (Video + Fade)' first.");
                return;
            }

            var directorGo = GameObject.Find("ImmersiveVideoDirector");
            var director = directorGo != null ? directorGo.GetComponent<ImmersiveVideoDirector>() : null;
            if (director == null)
            {
                Debug.LogError("[ImmersiveVideo] ImmersiveVideoDirector not found. Run 'ImmersiveVideo/Setup MVP (Video + Fade)' first.");
                return;
            }

            var material = GetOrCreateRevealMaterial();
            if (material == null)
                return;

            var geometryProvider = GetOrCreateGeometryProvider(xrOrigin, material);
            var scanRevealDirector = GetOrCreateScanRevealDirector(xrOrigin, fader);

            SetSerializedField(director, "m_GeometryProvider", geometryProvider);
            SetSerializedField(director, "m_ScanRevealDirector", scanRevealDirector);

            AssetDatabase.SaveAssets();

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ImmersiveVideo] TASK-10 wiring complete. config.introMode is still SimpleFade - " +
                "switch the ImmersiveVideoConfig asset's Intro Mode to ScanReveal in the Inspector to test it.");
        }

        const string k_RenderTexturePath = "Assets/ImmersiveVideo/Config/VideoRT.renderTexture";

        static Material GetOrCreateRevealMaterial()
        {
            var videoRT = AssetDatabase.LoadAssetAtPath<RenderTexture>(k_RenderTexturePath);
            if (videoRT == null)
            {
                Debug.LogError($"[ImmersiveVideo] {k_RenderTexturePath} not found. Run 'ImmersiveVideo/Setup MVP (Video + Fade)' first.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (material == null)
            {
                var shader = Shader.Find(k_ShaderName);
                if (shader == null)
                {
                    Debug.LogError($"[ImmersiveVideo] Shader '{k_ShaderName}' not found - check it compiled without errors.");
                    return null;
                }

                material = new Material(shader) { name = "SceneRevealSurface" };
                AssetDatabase.CreateAsset(material, k_MaterialPath);
            }

            // Bug fix: this was never wired up before, so _VideoTex silently used the
            // shader's "black" default - the reveal surfaces showed solid black instead of
            // the video, blocking the already-correctly-faded-in equirect layer behind them.
            material.SetTexture("_VideoTex", videoRT);
            EditorUtility.SetDirty(material);
            return material;
        }

        static SceneGeometryProvider GetOrCreateGeometryProvider(XROrigin xrOrigin, Material material)
        {
            var existing = xrOrigin.GetComponentInChildren<SceneGeometryProvider>(true);
            if (existing != null)
            {
                SetSerializedField(existing, "m_RevealMaterial", material);
                SetSerializedField(existing, "m_XROrigin", xrOrigin);
                return existing;
            }

            var go = new GameObject("SceneGeometryProvider");
            Undo.RegisterCreatedObjectUndo(go, "Create SceneGeometryProvider");
            go.transform.SetParent(xrOrigin.transform, false);

            var provider = go.AddComponent<SceneGeometryProvider>();
            SetSerializedField(provider, "m_RevealMaterial", material);
            SetSerializedField(provider, "m_XROrigin", xrOrigin);
            return provider;
        }

        static ScanRevealDirector GetOrCreateScanRevealDirector(XROrigin xrOrigin, CompositionLayerFader fader)
        {
            var go = GameObject.Find("ScanRevealDirector");
            if (go == null)
            {
                go = new GameObject("ScanRevealDirector");
                Undo.RegisterCreatedObjectUndo(go, "Create ScanRevealDirector");
            }

            var scanReveal = go.GetComponent<ScanRevealDirector>();
            if (scanReveal == null)
                scanReveal = go.AddComponent<ScanRevealDirector>();

            SetSerializedField(scanReveal, "m_SampleOriginTransform", xrOrigin.Camera.transform);
            SetSerializedField(scanReveal, "m_EquirectFader", fader);
            return scanReveal;
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
