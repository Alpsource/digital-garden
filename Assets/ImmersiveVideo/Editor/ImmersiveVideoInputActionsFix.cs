using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace ImmersiveVideo.Editor
{
    // Root-cause fix: "GameObject/XR/XR Origin (VR)" tries to auto-assign the "XRI Default
    // Input Actions" asset to the rig's InputActionManager, but that asset only exists
    // inside the XRI package's un-imported Samples~ folder - the search silently found
    // nothing, so InputActionManager.actionAssets has been empty this whole time. With no
    // Input Action anywhere referencing a controller/hand device layout, OpenXR never
    // suggests those interaction profile bindings to the runtime, so no controller or hand
    // device ever appears in InputSystem.devices - explaining why neither ray ever formed,
    // regardless of which driver script is used.
    static class ImmersiveVideoInputActionsFix
    {
        const string k_SamplePackage = "com.unity.xr.interaction.toolkit";
        const string k_SampleName = "Starter Assets";

        [MenuItem("ImmersiveVideo/Fix Missing Input Actions")]
        static void FixMissingInputActions()
        {
            var xrOriginGo = GameObject.Find("XR Origin (VR)");
            if (xrOriginGo == null)
            {
                Debug.LogError("[ImmersiveVideo] XR Origin (VR) not found.");
                return;
            }

            var inputActionManager = xrOriginGo.GetComponent<InputActionManager>();
            if (inputActionManager == null)
            {
                Debug.LogError("[ImmersiveVideo] XR Origin (VR) has no InputActionManager component.");
                return;
            }

            var asset = FindImportedDefaultActions();
            if (asset == null)
            {
                Debug.Log("[ImmersiveVideo] 'XRI Default Input Actions' not found in Assets - importing the 'Starter Assets' sample now.");
                if (!ImportStarterAssetsSample())
                {
                    Debug.LogError("[ImmersiveVideo] Failed to import the Starter Assets sample. Import it manually via " +
                        "Window > Package Manager > XR Interaction Toolkit > Samples > Starter Assets, then run this again.");
                    return;
                }
                AssetDatabase.Refresh();
                asset = FindImportedDefaultActions();
            }

            if (asset == null)
            {
                Debug.LogError("[ImmersiveVideo] Still couldn't find 'XRI Default Input Actions' after importing the sample. " +
                    "Check Assets/Samples/XR Interaction Toolkit/.../Starter Assets/ manually.");
                return;
            }

            var so = new SerializedObject(inputActionManager);
            var prop = so.FindProperty("m_ActionAssets");
            if (prop == null)
            {
                Debug.LogError("[ImmersiveVideo] Serialized field 'm_ActionAssets' not found on InputActionManager - " +
                    "check the field name in this XRI version and assign it manually in the Inspector instead.");
                return;
            }

            var alreadyPresent = false;
            for (var i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(i).objectReferenceValue == asset)
                {
                    alreadyPresent = true;
                    break;
                }
            }
            if (!alreadyPresent)
            {
                prop.InsertArrayElementAtIndex(prop.arraySize);
                prop.GetArrayElementAtIndex(prop.arraySize - 1).objectReferenceValue = asset;
            }
            so.ApplyModifiedProperties();

            AssetDatabase.SaveAssets();
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[ImmersiveVideo] Assigned '{asset.name}' to XR Origin's InputActionManager. " +
                "Rebuild and test - controller/hand rays should now actually form.");
        }

        static InputActionAsset FindImportedDefaultActions()
        {
            var guids = AssetDatabase.FindAssets("\"XRI Default Input Actions\" t:InputActionAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                if (asset != null && asset.name.Equals("XRI Default Input Actions", System.StringComparison.OrdinalIgnoreCase))
                    return asset;
            }
            return null;
        }

        static bool ImportStarterAssetsSample()
        {
            var samples = Sample.FindByPackage(k_SamplePackage, null);
            var sample = samples.FirstOrDefault(s => s.displayName == k_SampleName);
            if (sample.displayName != k_SampleName)
            {
                Debug.LogError($"[ImmersiveVideo] Could not find the '{k_SampleName}' sample for {k_SamplePackage}.");
                return false;
            }
            return sample.Import(Sample.ImportOptions.OverridePreviousImports);
        }
    }
}
