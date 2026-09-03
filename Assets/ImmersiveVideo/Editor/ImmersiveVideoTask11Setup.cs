using System.Linq;
using ImmersiveVideo;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace ImmersiveVideo.Editor
{
    // TASK-11 wiring: adds a controller Near-Far Interactor (guaranteed-working path)
    // and a hand-pinch Near-Far Interactor driven by the OpenXR Hand Interaction Profile
    // (new/unverified - confirm on-device), a world-space Start button + IdleController,
    // and wires the new Director fields. Does not touch anything from TASK-02/MVP/TASK-10.
    static class ImmersiveVideoTask11Setup
    {
        [MenuItem("ImmersiveVideo/Setup TASK-11 (Start Button)")]
        static void SetupTask11()
        {
            var xrOriginGo = GameObject.Find("XR Origin (VR)");
            var xrOrigin = xrOriginGo != null ? xrOriginGo.GetComponent<XROrigin>() : null;
            if (xrOrigin == null)
            {
                Debug.LogError("[ImmersiveVideo] XR Origin (VR) not found. Run 'ImmersiveVideo/Setup Scene' first.");
                return;
            }

            var videoLayer = GameObject.Find("VideoLayer");
            var directorGo = GameObject.Find("ImmersiveVideoDirector");
            var director = directorGo != null ? directorGo.GetComponent<ImmersiveVideoDirector>() : null;
            if (videoLayer == null || director == null)
            {
                Debug.LogError("[ImmersiveVideo] VideoLayer/ImmersiveVideoDirector not found. Run 'ImmersiveVideo/Setup MVP (Video + Fade)' first.");
                return;
            }

            var config = AssetDatabase.LoadAssetAtPath<ImmersiveVideoConfig>("Assets/ImmersiveVideo/Config/ImmersiveVideoConfig.asset");
            if (config == null)
            {
                Debug.LogError("[ImmersiveVideo] ImmersiveVideoConfig.asset not found.");
                return;
            }

            // The asset on disk predates the TASK-11 fields (loop/EndBehaviour removed,
            // button/outro/replay fields added) - it still loads fine (missing fields get
            // their C# defaults) but force a resave so the YAML on disk actually matches
            // the current schema instead of silently relying on that fallback.
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            // Self-heal duplicate interactors left over from earlier runs of this command
            // (before the drivers existed, or from repeated partial runs) before creating
            // or looking up anything else.
            RemoveDuplicateInteractors(xrOrigin, k_ControllerInteractorName);
            RemoveDuplicateInteractors(xrOrigin, k_HandInteractorName);

            var controllerInteractor = GetOrCreateControllerInteractor(xrOrigin);
            var handInteractor = GetOrCreateHandInteractor(xrOrigin);

            var canvasGo = GetOrCreateButtonCanvas(xrOrigin, config);
            var button = canvasGo.GetComponentInChildren<Button>(true);
            var idleController = GetOrCreateIdleController(canvasGo, button, xrOrigin, config);

            var arCameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
            var audioSource = videoLayer.GetComponent<AudioSource>();

            SetSerializedField(director, "m_IdleController", idleController);
            SetSerializedField(director, "m_ArCameraManager", arCameraManager);
            SetSerializedField(director, "m_AudioSource", audioSource);
            SetSerializedField(director, "m_VideoLayer", videoLayer.transform);

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ImmersiveVideo] TASK-11 wiring complete. Controller ray interactor is guaranteed to work; " +
                "the hand-pinch interactor uses the Hand Interaction Profile feature and needs on-device verification. " +
                "Remember to enable 'Hand Interaction Profile' in XR Plug-in Management > OpenXR if you want hand input.");
        }

        const string k_ControllerInteractorName = "Near-Far Interactor (Controller)";
        const string k_HandInteractorName = "Near-Far Interactor (Hand Pinch)";

        // Collapses any duplicate-named Near-Far Interactors under the XR Origin down to
        // one, preferring one that already has its driver attached. Handles the case where
        // several partial/earlier runs each created their own copy.
        static void RemoveDuplicateInteractors(XROrigin xrOrigin, string name)
        {
            var matches = xrOrigin.GetComponentsInChildren<NearFarInteractor>(true)
                .Where(i => i.name == name)
                .Select(i => i.gameObject)
                .ToList();

            if (matches.Count == 0)
                return;

            var keeper = matches.FirstOrDefault(go => go.GetComponent<ControllerInteractionDriver>() != null
                || go.GetComponent<HandInteractionDriver>() != null) ?? matches[0];

            foreach (var go in matches)
            {
                if (go == keeper)
                    continue;
                Debug.Log($"[ImmersiveVideo] Removing duplicate '{go.name}' left over from an earlier setup run.");
                Undo.DestroyObjectImmediate(go);
            }

            // Always run, even with a single surviving GameObject - it may itself carry
            // duplicate driver components from an earlier bug (see fix history).
            RemoveDuplicateComponents<ControllerInteractionDriver>(keeper);
            RemoveDuplicateComponents<HandInteractionDriver>(keeper);
        }

        // [DisallowMultipleComponent] only stops new duplicates from being added going
        // forward - it doesn't retroactively clean up ones a scene already has (e.g. from
        // before that attribute existed).
        static void RemoveDuplicateComponents<T>(GameObject go) where T : Component
        {
            var components = go.GetComponents<T>();
            for (var i = 1; i < components.Length; i++)
            {
                Debug.Log($"[ImmersiveVideo] Removing duplicate {typeof(T).Name} on '{go.name}'.");
                Undo.DestroyObjectImmediate(components[i]);
            }
        }

        static GameObject GetOrCreateControllerInteractor(XROrigin xrOrigin)
        {
            var existingDriver = xrOrigin.GetComponentInChildren<ControllerInteractionDriver>(true);
            if (existingDriver != null)
                return existingDriver.gameObject;

            // A previous run of this command (before ControllerInteractionDriver existed)
            // may have already created the bare interactor under this exact name - retrofit
            // it instead of creating a second, duplicate-named one.
            var existingGo = GameObject.Find(k_ControllerInteractorName);
            GameObject created;
            if (existingGo != null && existingGo.GetComponent<NearFarInteractor>() != null)
            {
                created = existingGo;
            }
            else
            {
                Selection.activeGameObject = xrOrigin.gameObject;
                EditorApplication.ExecuteMenuItem("GameObject/XR/Near-Far Interactor");
                created = Selection.activeGameObject;
                if (created == null)
                {
                    Debug.LogError("[ImmersiveVideo] 'GameObject/XR/Near-Far Interactor' did not create an object as expected.");
                    return null;
                }
                created.name = k_ControllerInteractorName;
            }

            var interactor = created.GetComponent<NearFarInteractor>();
            var driver = created.GetComponent<ControllerInteractionDriver>();
            if (driver == null)
                driver = Undo.AddComponent<ControllerInteractionDriver>(created);
            SetSerializedField(driver, "m_Interactor", interactor);
            // m_Hand left at its default (RightHand).
            return created;
        }

        static GameObject GetOrCreateHandInteractor(XROrigin xrOrigin)
        {
            var existingDriver = xrOrigin.GetComponentInChildren<HandInteractionDriver>(true);
            if (existingDriver != null)
                return existingDriver.gameObject;

            var existingGo = GameObject.Find(k_HandInteractorName);
            GameObject created;
            if (existingGo != null && existingGo.GetComponent<NearFarInteractor>() != null)
            {
                created = existingGo;
            }
            else
            {
                Selection.activeGameObject = xrOrigin.gameObject;
                EditorApplication.ExecuteMenuItem("GameObject/XR/Near-Far Interactor");
                created = Selection.activeGameObject;
                if (created == null)
                {
                    Debug.LogError("[ImmersiveVideo] 'GameObject/XR/Near-Far Interactor' did not create an object as expected (hand path).");
                    return null;
                }
                created.name = k_HandInteractorName;
            }

            var interactor = created.GetComponent<NearFarInteractor>();
            var driver = created.GetComponent<HandInteractionDriver>();
            if (driver == null)
                driver = Undo.AddComponent<HandInteractionDriver>(created);
            SetSerializedField(driver, "m_Interactor", interactor);
            // m_Hand left at its default (RightHand).
            return created;
        }

        const float k_CanvasUnits = 500f; // arbitrary "pixel" size, scaled down to real-world meters below

        static GameObject GetOrCreateButtonCanvas(XROrigin xrOrigin, ImmersiveVideoConfig config)
        {
            var canvasGo = GameObject.Find("StartButtonCanvas");
            if (canvasGo == null)
            {
                Selection.activeGameObject = null;
                EditorApplication.ExecuteMenuItem("GameObject/XR/UI Canvas");
                canvasGo = Selection.activeGameObject;
                if (canvasGo == null)
                {
                    Debug.LogError("[ImmersiveVideo] 'GameObject/XR/UI Canvas' did not create an object as expected.");
                    return null;
                }
                canvasGo.name = "StartButtonCanvas";

                var canvas = canvasGo.GetComponent<Canvas>();
                if (canvas != null)
                    canvas.worldCamera = xrOrigin.Camera;

                var canvasRect = canvasGo.GetComponent<RectTransform>();
                canvasRect.sizeDelta = new Vector2(k_CanvasUnits, k_CanvasUnits);
                var scale = config.buttonDiameter / k_CanvasUnits;
                canvasGo.transform.localScale = new Vector3(scale, scale, scale);
            }

            var buttonGo = GameObject.Find("StartButton");
            if (buttonGo == null || buttonGo.transform.parent != canvasGo.transform)
            {
                buttonGo = new GameObject("StartButton", typeof(RectTransform), typeof(Image), typeof(Button));
                Undo.RegisterCreatedObjectUndo(buttonGo, "Create StartButton");
                buttonGo.transform.SetParent(canvasGo.transform, false);
                var buttonRect = buttonGo.GetComponent<RectTransform>();
                buttonRect.anchorMin = Vector2.zero;
                buttonRect.anchorMax = Vector2.one;
                buttonRect.sizeDelta = Vector2.zero;

                var image = buttonGo.GetComponent<Image>();
                image.color = new Color(0.15f, 0.55f, 0.95f, 1f); // opaque - see shader/UI blend note in the addendum
                image.raycastTarget = true;

                var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                Undo.RegisterCreatedObjectUndo(textGo, "Create StartButton Label");
                textGo.transform.SetParent(buttonGo.transform, false);
                var textRect = textGo.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.sizeDelta = Vector2.zero;

                var text = textGo.GetComponent<Text>();
                text.text = "Basla";
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.fontSize = 96;
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.raycastTarget = false; // must be off - see uGUI gotchas in the addendum
            }

            // Fallback trigger path: a plain 3D collider + XRSimpleInteractable, independent
            // of the uGUI/EventSystem/XRUIInputModule/TrackedDeviceGraphicRaycaster chain.
            // Retrofit onto an existing button too - always runs, not just on fresh creation.
            var boxCollider = buttonGo.GetComponent<BoxCollider>();
            if (boxCollider == null)
                boxCollider = Undo.AddComponent<BoxCollider>(buttonGo);
            boxCollider.isTrigger = false; // XRBaseInteractable's auto-populate skips trigger colliders
            boxCollider.size = new Vector3(k_CanvasUnits, k_CanvasUnits, 10f);
            if (buttonGo.GetComponent<XRSimpleInteractable>() == null)
                Undo.AddComponent<XRSimpleInteractable>(buttonGo);

            return canvasGo;
        }

        static IdleController GetOrCreateIdleController(GameObject canvasGo, Button button, XROrigin xrOrigin, ImmersiveVideoConfig config)
        {
            var idleController = canvasGo.GetComponent<IdleController>();
            if (idleController == null)
                idleController = Undo.AddComponent<IdleController>(canvasGo);

            SetSerializedField(idleController, "m_HeadTransform", xrOrigin.Camera.transform);
            SetSerializedField(idleController, "m_Canvas", canvasGo);
            SetSerializedField(idleController, "m_Button", button);
            SetSerializedField(idleController, "m_ButtonRoot", canvasGo.transform);
            SetSerializedField(idleController, "m_Config", config);
            SetSerializedField(idleController, "m_SelectInteractable", canvasGo.GetComponentInChildren<XRSimpleInteractable>(true));
            return idleController;
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
