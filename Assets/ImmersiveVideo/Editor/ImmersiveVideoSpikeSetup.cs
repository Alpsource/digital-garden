using System.IO;
using ImmersiveVideo.Spike;
using Unity.XR.CompositionLayers.Extensions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ImmersiveVideo.Editor
{
    // TASK-03 spike setup: generates a static test equirect texture, assigns it to
    // VideoLayer's Source Textures extension, and wires up a FadeSpike controller.
    // Throwaway tooling alongside the throwaway FadeSpike component.
    static class ImmersiveVideoSpikeSetup
    {
        const string k_TexturePath = "Assets/ImmersiveVideo/Spike/TestEquirect.png";

        [MenuItem("ImmersiveVideo/Setup Fade Spike")]
        static void SetupFadeSpike()
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

            texturesExt.LeftTexture = GetOrCreateTestTexture();

            var controllerGo = GameObject.Find("FadeSpikeController");
            if (controllerGo == null)
            {
                controllerGo = new GameObject("FadeSpikeController", typeof(FadeSpike));
                Undo.RegisterCreatedObjectUndo(controllerGo, "Create FadeSpikeController");
            }

            var spike = controllerGo.GetComponent<FadeSpike>();
            var so = new SerializedObject(spike);
            so.FindProperty("m_ColorScaleBias").objectReferenceValue = colorScaleBias;
            so.ApplyModifiedProperties();

            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[ImmersiveVideo] Fade spike ready: test texture assigned, FadeSpikeController wired up. Build to device now.");
        }

        static Texture2D GetOrCreateTestTexture()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath);
            if (existing != null)
                return existing;

            const int width = 512, height = 256;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var hue = (float)x / width;
                    var color = Color.HSVToRGB(hue, 0.8f, 0.9f);
                    var checker = ((x / 32) + (y / 32)) % 2 == 0;
                    if (checker)
                        color *= 0.7f;
                    pixels[y * width + x] = color;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(k_TexturePath)!);
            File.WriteAllBytes(k_TexturePath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(k_TexturePath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(k_TexturePath);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath);
        }
    }
}
