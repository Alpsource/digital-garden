using System.IO;
using System.Linq;
using Unity.XR.CompositionLayers;
using Unity.XR.CompositionLayers.Extensions;
using Unity.XR.CompositionLayers.Layers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;

namespace ImmersiveVideo.Editor
{
    // TASK-02 scene setup. Idempotent: safe to run multiple times on the same scene.
    // Builds the hierarchy using the packages' own GameObject-creation menu items
    // instead of hand-authoring components, so the resulting objects match whatever
    // that package version actually expects.
    static class ImmersiveVideoSceneSetup
    {
        const string k_TargetSceneName = "PassThroughVideoScene";
        const string k_LayersParentName = "Composition Layers";
        const string k_PassthroughLayerName = "PassthroughLayer";
        const string k_VideoLayerName = "VideoLayer";

        [MenuItem("ImmersiveVideo/Setup Scene")]
        static void SetupScene()
        {
            var scene = SceneManager.GetActiveScene();
            var sceneName = Path.GetFileNameWithoutExtension(scene.path);
            if (sceneName != k_TargetSceneName)
            {
                var proceed = EditorUtility.DisplayDialog(
                    "Immersive Video Setup",
                    $"Active scene is '{sceneName}', not '{k_TargetSceneName}'. Continue anyway?",
                    "Continue", "Cancel");
                if (!proceed)
                    return;
            }

            var xrOrigin = SetUpXROrigin();
            SetUpArSession();
            SetUpCamera(xrOrigin);
            var layersParent = GetOrCreateEmpty(k_LayersParentName, null);
            SetUpPassthroughLayer(layersParent.transform);
            SetUpVideoLayer(layersParent.transform);
            AddSceneToBuildSettings(scene.path);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ImmersiveVideo] Scene setup complete and saved: " + scene.path);
        }

        static GameObject SetUpXROrigin()
        {
            var existing = GameObject.Find("XR Origin (VR)");
            if (existing != null)
                return existing;

            Selection.activeGameObject = null;
            EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (VR)");
            var created = Selection.activeGameObject;
            if (created == null)
                Debug.LogError("[ImmersiveVideo] 'GameObject/XR/XR Origin (VR)' did not create an object as expected.");
            return created;
        }

        static void SetUpArSession()
        {
            if (UnityEngine.Object.FindFirstObjectByType<ARSession>() != null)
                return;

            var go = new GameObject("AR Session", typeof(ARSession));
            Undo.RegisterCreatedObjectUndo(go, "Create AR Session");
        }

        static void SetUpCamera(GameObject xrOrigin)
        {
            if (xrOrigin == null)
                return;

            var cam = xrOrigin.GetComponentInChildren<Camera>(true);
            if (cam == null)
            {
                Debug.LogWarning("[ImmersiveVideo] Could not find a Camera under XR Origin (VR) to configure for passthrough.");
                return;
            }

            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);

            if (cam.GetComponent<ARCameraManager>() == null)
                Undo.AddComponent<ARCameraManager>(cam.gameObject);

            var arBackground = cam.GetComponent<ARCameraBackground>();
            if (arBackground != null)
                Undo.DestroyObjectImmediate(arBackground);

            RemoveStrayMainCameras(cam);
        }

        // The scene may already contain a leftover default "Main Camera" (e.g. from a blank
        // scene template) tagged MainCamera. If it coexists with the XR Origin's camera,
        // Camera.main resolution is ambiguous and composition-layer validation (and passthrough
        // itself) may end up looking at the wrong, non-transparent camera. Remove any other
        // MainCamera-tagged camera outside the XR Origin hierarchy.
        static void RemoveStrayMainCameras(Camera keep)
        {
            var allCameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var other in allCameras)
            {
                if (other == keep)
                    continue;
                if (!other.CompareTag("MainCamera"))
                    continue;

                Debug.Log($"[ImmersiveVideo] Removing stray MainCamera-tagged camera '{other.name}' " +
                    "that would otherwise conflict with the XR Origin camera.");
                Undo.DestroyObjectImmediate(other.gameObject);
            }
        }

        static GameObject GetOrCreateEmpty(string name, Transform parent)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
                return existing;

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            if (parent != null)
                go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return go;
        }

        static void SetUpPassthroughLayer(Transform parent)
        {
            // Also check for the un-renamed default name, in case a previous partial run
            // created it but failed before the rename step.
            var go = GameObject.Find(k_PassthroughLayerName) ?? GameObject.Find("Passthrough Layer");
            if (go == null)
            {
                Selection.activeGameObject = null;
                EditorApplication.ExecuteMenuItem("GameObject/XR/Composition Layers/Meta Passthrough Layer");
                go = Selection.activeGameObject;
                // The meta-openxr package's "Meta Passthrough Layer" menu command does not set
                // Selection after creating the object (unlike the other layer-creation commands),
                // so fall back to finding it by the literal name that command hardcodes.
                if (go == null)
                    go = GameObject.Find("Passthrough Layer");
                if (go == null)
                {
                    Debug.LogError("[ImmersiveVideo] 'GameObject/XR/Composition Layers/Meta Passthrough Layer' did not create an object as expected.");
                    return;
                }
            }

            go.name = k_PassthroughLayerName;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var layer = go.GetComponent<CompositionLayer>();
            if (layer != null)
                layer.Order = -2;
        }

        static void SetUpVideoLayer(Transform parent)
        {
            var go = GameObject.Find(k_VideoLayerName);
            if (go == null)
            {
                Selection.activeGameObject = null;
                EditorApplication.ExecuteMenuItem("GameObject/XR/Composition Layers/Equirect Layer");
                go = Selection.activeGameObject;
                if (go == null)
                {
                    Debug.LogError("[ImmersiveVideo] 'GameObject/XR/Composition Layers/Equirect Layer' did not create an object as expected.");
                    return;
                }
                go.name = k_VideoLayerName;
            }

            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var layer = go.GetComponent<CompositionLayer>();
            if (layer != null)
                layer.Order = -1;

            // EquirectMeshLayerData default Radius is already 100 (there is no "0 = infinite" special
            // case in this package version), which matches the spec's fallback suggestion.
            if (layer != null && layer.LayerData is EquirectMeshLayerData equirect)
            {
                equirect.Radius = 100f;

                // com.unity.xr.compositionlayers 2.5.0's own known-issues.md: "When running your
                // application on an Android HMD with the Equirect layer set to a 360 Central Angle,
                // you may see a vertical 1px seam in the sphere." Tried backing off to 359.9 (a gap)
                // first - that made the seam MORE visible (an empty gap instead of a hairline), even
                // in the Editor. Trying slight overlap instead (>360) since the setter does not clamp.
                // Unverified whether the native compositor accepts/handles this the same way; test
                // in-headset before trusting it.
                equirect.CentralHorizontalAngleInDegrees = 360.1f;
            }

            // TexturesExtension is auto-added by the "Equirect Layer" menu item (it's listed as a
            // SuggestedExtenstionType on EquirectMeshLayerData). ColorScaleBiasExtension is not, so add it here.
            var colorScaleBias = go.GetComponent<ColorScaleBiasExtension>();
            if (colorScaleBias == null)
                colorScaleBias = Undo.AddComponent<ColorScaleBiasExtension>(go);
            colorScaleBias.Scale = new Vector4(1f, 1f, 1f, 0f);
            colorScaleBias.Bias = Vector4.zero;
        }

        static void AddSceneToBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Any(s => s.path == scenePath))
                return;

            var newScenes = scenes.Append(new EditorBuildSettingsScene(scenePath, true)).ToArray();
            EditorBuildSettings.scenes = newScenes;
        }
    }
}
