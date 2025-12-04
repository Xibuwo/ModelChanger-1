using HarmonyLib;
using UnityEngine;
using Bulbul;
using System.Linq;
using System.Collections.Generic;
using System.IO;

namespace ChillWithYou.ModelChanger.Patches
{
    [HarmonyPatch(typeof(HeroineService), "Setup")]
    internal static class HeroineModelSwapperPatch
    {
        static void Postfix(HeroineService __instance)
        {
            string selectedModel = ModelChangerPlugin.Cfg_CurrentModel.Value;
            if (selectedModel == "Default")
            {
                if (ModelChangerPlugin.CurrentCharacterObject != null)
                {
                    Object.Destroy(ModelChangerPlugin.CurrentCharacterObject);
                    ModelChangerPlugin.CurrentCharacterObject = null;
                }
                return;
            }

            var modelData = ModelChangerPlugin.ModelRegistry.GetModel(selectedModel);
            if (modelData == null || !modelData.IsCustom)
            {
                return;
            }

            if (ModelChangerPlugin.CurrentCharacterObject != null &&
                ModelChangerPlugin.CurrentCharacterObject.transform.parent == __instance.transform)
            {
                Object.Destroy(ModelChangerPlugin.CurrentCharacterObject);
                ModelChangerPlugin.CurrentCharacterObject = null;
            }

            ApplyModelToCharacter(__instance.gameObject, modelData);
        }

        // Made public so UI can call it directly
        public static void ApplyModelToCharacter(GameObject rootObject, ModelData modelData)
        {
            try
            {
                // Load the FBX mesh
                var meshes = AssetLoader.LoadFBX(modelData.FBXPath);
                if (meshes == null || meshes.Length == 0)
                {
                    ModelChangerPlugin.Log?.LogError($"Failed to load mesh from {modelData.FBXPath}");
                    return;
                }

                // Hide Original Model
                foreach (var renderer in rootObject.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.gameObject != rootObject)
                    {
                        renderer.enabled = false;
                    }
                }

                // Create new model instance
                var customModel = new GameObject(modelData.Name);
                customModel.transform.SetParent(rootObject.transform, false);
                customModel.transform.localPosition = Vector3.zero;
                customModel.transform.localRotation = Quaternion.identity;
                customModel.transform.localScale = Vector3.one;

                // Build the mesh with bone mapping from the original character
                var sourceMesh = meshes[0];
                var customMesh = AssetLoader.BuildMesh(sourceMesh, new AssetLoader.ArmatureData(rootObject));

                // Set up the skinned mesh renderer
                var skinnedRenderer = customModel.AddComponent<SkinnedMeshRenderer>();
                skinnedRenderer.sharedMesh = customMesh;

                // Copy bone references from original character
                var originalRenderer = rootObject.GetComponentInChildren<SkinnedMeshRenderer>();
                if (originalRenderer != null)
                {
                    skinnedRenderer.bones = originalRenderer.bones;
                    skinnedRenderer.rootBone = originalRenderer.rootBone;
                    skinnedRenderer.localBounds = originalRenderer.localBounds;
                }

                // Apply material with texture
                var material = new Material(Shader.Find("Standard"));

                // Load and apply the main texture
                if (!string.IsNullOrEmpty(modelData.TexturePath) && File.Exists(modelData.TexturePath))
                {
                    var texture = ImageLoader.LoadTexture(modelData.TexturePath);
                    if (texture != null)
                    {
                        material.mainTexture = texture;
                        ModelChangerPlugin.Log?.LogInfo($"Loaded texture: {Path.GetFileName(modelData.TexturePath)}");
                    }
                }

                skinnedRenderer.material = material;

                ModelChangerPlugin.CurrentCharacterObject = customModel;
                ModelChangerPlugin.Log?.LogInfo($"Successfully loaded model: {modelData.Name}");
            }
            catch (System.Exception ex)
            {
                ModelChangerPlugin.Log?.LogError($"Failed to load custom model '{modelData.Name}': {ex}");
            }
        }
    }
}