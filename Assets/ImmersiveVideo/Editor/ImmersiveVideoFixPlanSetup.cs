using ImmersiveVideo;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ImmersiveVideo.Editor
{
    // quest3-button-FIX-PLAN.md, ADIM 1 + ADIM 4: attaches the on-device input dump
    // (dev/editor builds only) and wires the gaze-dwell fallback button so the Start
    // flow is testable independent of controller/hand input while ADIM 2/3 are worked on.
    // Run after 'ImmersiveVideo/Setup TASK-11 (Start Button)'.
    static class ImmersiveVideoFixPlanSetup
    {
        [MenuItem("ImmersiveVideo/Setup FIX-PLAN Debug Tools (ADIM 1+4)")]
        static void SetupFixPlanDebugTools()
        {
            var xrOriginGo = GameObject.Find("XR Origin (VR)");
            var xrOrigin = xrOriginGo != null ? xrOriginGo.GetComponent<XROrigin>() : null;
            if (xrOrigin == null)
            {
                Debug.LogError("[ImmersiveVideo] XR Origin (VR) not found. Run 'ImmersiveVideo/Setup Scene' first.");
                return;
            }

            if (xrOriginGo.GetComponent<XRInputDump>() == null)
                Undo.AddComponent<XRInputDump>(xrOriginGo);

            var buttonGo = GameObject.Find("StartButton");
            var canvasGo = GameObject.Find("StartButtonCanvas");
            var idleController = canvasGo != null ? canvasGo.GetComponent<IdleController>() : null;
            if (buttonGo == null || idleController == null)
            {
                Debug.LogError("[ImmersiveVideo] StartButton/IdleController not found. Run 'ImmersiveVideo/Setup TASK-11 (Start Button)' first.");
                return;
            }

            // StartButton already carries a non-trigger BoxCollider (added by the TASK-11
            // setup for the XRSimpleInteractable fallback) - reuse it for the gaze raycast
            // instead of adding a second collider.
            var gazeButton = buttonGo.GetComponent<GazeDwellButton>();
            if (gazeButton == null)
                gazeButton = Undo.AddComponent<GazeDwellButton>(buttonGo);

            SetSerializedField(idleController, "m_GazeDwellButton", gazeButton);

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ImmersiveVideo] FIX-PLAN debug tools wired: XRInputDump on XR Origin (logs every 5s in dev/editor builds), " +
                "GazeDwellButton on StartButton (1.5s head-gaze dwell triggers Start independent of controller/hand input).");
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

        // quest3-button-FIX-PLAN.md, ADIM 2: swap the hand-rolled polling drivers for
        // Unity's own pre-wired, pre-tested controller rig. 'Left Controller'/'Right
        // Controller' (TrackedPoseDriver + a nested NearFarInteractor, all bound to "XRI
        // Default Input Actions") are NOT standalone prefab assets - they only exist nested
        // inside the sample's "XR Origin (XR Rig).prefab". Rather than hand-authoring
        // equivalent Input Action wiring from scratch (error-prone, exactly what got us
        // into this mess), extract real copies of those two GameObjects out of that rig via
        // LoadPrefabContents and parent them under our own Camera Offset - camera/passthrough
        // untouched, old drivers disabled (not deleted) alongside them.
        const string k_RigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        [MenuItem("ImmersiveVideo/Setup FIX-PLAN Controllers (ADIM 2)")]
        static void SetupFixPlanControllers()
        {
            var xrOriginGo = GameObject.Find("XR Origin (VR)");
            var xrOrigin = xrOriginGo != null ? xrOriginGo.GetComponent<XROrigin>() : null;
            if (xrOrigin == null)
            {
                Debug.LogError("[ImmersiveVideo] XR Origin (VR) not found. Run 'ImmersiveVideo/Setup Scene' first.");
                return;
            }

            var cameraOffsetGo = xrOrigin.CameraFloorOffsetObject;
            if (cameraOffsetGo == null)
            {
                Debug.LogError("[ImmersiveVideo] XR Origin has no Camera Offset object.");
                return;
            }
            var cameraOffset = cameraOffsetGo.transform;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(k_RigPrefabPath) == null)
            {
                Debug.LogError($"[ImmersiveVideo] '{k_RigPrefabPath}' not found. Import the 'Starter Assets' sample " +
                    "(Window > Package Manager > XR Interaction Toolkit > Samples) first.");
                return;
            }

            DisableGameObject("Near-Far Interactor (Controller)");
            DisableGameObject("Near-Far Interactor (Hand Pinch)");

            EnsureControllerCopied(cameraOffset, "Left Controller");
            EnsureControllerCopied(cameraOffset, "Right Controller");

            var eventSystems = Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(FindObjectsSortMode.None);
            if (eventSystems.Length != 1)
                Debug.LogWarning($"[ImmersiveVideo] Expected exactly 1 EventSystem in the scene, found {eventSystems.Length}. " +
                    "Multiple EventSystems will fight over input focus.");

            RemoveButtonBoxColliderFallback();

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ImmersiveVideo] FIX-PLAN ADIM 2 complete: Left/Right Controller (Unity's pre-wired NearFarInteractor + " +
                "TrackedPoseDriver, bound to XRI Default Input Actions) are now under Camera Offset. Old custom drivers disabled, not deleted. " +
                "StartButton's XRSimpleInteractable fallback removed (uGUI Button + TrackedDeviceGraphicRaycaster handles selection now); " +
                "its BoxCollider stays for the GazeDwellButton fallback.");
        }

        static void DisableGameObject(string name)
        {
            var go = GameObject.Find(name);
            if (go == null || !go.activeSelf)
                return;
            Undo.RecordObject(go, $"Disable {name}");
            go.SetActive(false);
        }

        static void EnsureControllerCopied(Transform cameraOffset, string childName)
        {
            var existing = cameraOffset.Find(childName);
            if (existing != null)
                return;

            var contentsRoot = PrefabUtility.LoadPrefabContents(k_RigPrefabPath);
            try
            {
                var source = FindChildRecursive(contentsRoot.transform, childName);
                if (source == null)
                {
                    Debug.LogError($"[ImmersiveVideo] '{childName}' not found inside '{k_RigPrefabPath}'.");
                    return;
                }

                var copy = Object.Instantiate(source.gameObject, cameraOffset);
                copy.name = childName;
                copy.transform.localPosition = source.localPosition;
                copy.transform.localRotation = source.localRotation;
                copy.transform.localScale = source.localScale;
                Undo.RegisterCreatedObjectUndo(copy, $"Create {childName}");

                // The stylized visual controller model (flat, semi-transparent quad parts)
                // renders right next to the camera in the Editor Game View with no real
                // tracking data - and we don't need it for a single-button use case anyway,
                // only the ray/interactor matters. Keep it in the hierarchy (in case someone
                // wants it later) but off by default.
                var visual = copy.transform.Find($"{childName} Visual");
                if (visual != null)
                    visual.gameObject.SetActive(false);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contentsRoot);
            }
        }

        static Transform FindChildRecursive(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name)
                    return child;
                var found = FindChildRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        // ADIM 2 explicitly calls out removing the BoxCollider+XRSimpleInteractable
        // pairing from the button - uGUI only needs Canvas + TrackedDeviceGraphicRaycaster,
        // and that parallel trigger path is no longer needed once real interactors exist.
        // The BoxCollider itself stays: GazeDwellButton (ADIM 4) raycasts against it.
        static void RemoveButtonBoxColliderFallback()
        {
            var buttonGo = GameObject.Find("StartButton");
            if (buttonGo == null)
                return;

            var simpleInteractable = buttonGo.GetComponent<XRSimpleInteractable>();
            if (simpleInteractable == null)
                return;

            var canvasGo = GameObject.Find("StartButtonCanvas");
            var idleController = canvasGo != null ? canvasGo.GetComponent<IdleController>() : null;
            if (idleController != null)
                SetSerializedField(idleController, "m_SelectInteractable", null);

            Undo.DestroyObjectImmediate(simpleInteractable);
        }
    }
}
