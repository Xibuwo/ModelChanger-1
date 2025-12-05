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

                // Re-enable original model
                var characterRoot = FindCharacterRoot(__instance.gameObject);
                if (characterRoot != null)
                {
                    foreach (var renderer in characterRoot.GetComponentsInChildren<Renderer>(true))
                    {
                        renderer.enabled = true;
                    }
                }
                return;
            }

            var modelData = ModelChangerPlugin.ModelRegistry.GetModel(selectedModel);
            if (modelData == null || !modelData.IsCustom)
            {
                return;
            }

            if (ModelChangerPlugin.CurrentCharacterObject != null)
            {
                Object.Destroy(ModelChangerPlugin.CurrentCharacterObject);
                ModelChangerPlugin.CurrentCharacterObject = null;
            }

            ApplyModelToCharacter(__instance.gameObject, modelData);
        }

        private static Transform FindCharacterRoot(GameObject rootObject)
        {
            // Try the expected path first
            Transform characterRoot = rootObject.transform.Find("Character/Character");
            if (characterRoot != null) return characterRoot;

            // Search for any transform with SkinnedMeshRenderer children
            var skinnedRenderers = rootObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedRenderers.Length > 0)
            {
                // Find the common parent of all skinned renderers
                Transform parent = skinnedRenderers[0].transform.parent;
                if (parent != null)
                {
                    return parent;
                }
            }

            return null;
        }

        public static void ApplyModelToCharacter(GameObject rootObject, ModelData modelData)
        {
            try
            {
                // Find the character root
                Transform characterRoot = FindCharacterRoot(rootObject);
                if (characterRoot == null)
                {
                    ModelChangerPlugin.Log?.LogError("Cannot find character root");
                    return;
                }

                ModelChangerPlugin.Log?.LogInfo($"Found character root: {characterRoot.name}");

                // Get the original SkinnedMeshRenderer from any child
                var originalRenderers = characterRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                if (originalRenderers.Length == 0)
                {
                    ModelChangerPlugin.Log?.LogError("No SkinnedMeshRenderer found in character hierarchy");
                    return;
                }

                var originalRenderer = originalRenderers[0];
                ModelChangerPlugin.Log?.LogInfo($"Reference renderer: {originalRenderer.gameObject.name}, Bones: {originalRenderer.bones.Length}");

                // Log bone names for debugging
                ModelChangerPlugin.Log?.LogInfo("Game skeleton bones:");
                for (int i = 0; i < originalRenderer.bones.Length && i < 20; i++)
                {
                    ModelChangerPlugin.Log?.LogInfo($"  [{i}] {originalRenderer.bones[i].name}");
                }

                // Load the FBX mesh
                var meshes = AssetLoader.LoadFBX(modelData.FBXPath);
                if (meshes == null || meshes.Length == 0)
                {
                    ModelChangerPlugin.Log?.LogError($"Failed to load mesh from {modelData.FBXPath}");
                    return;
                }

                // Hide all original renderers
                foreach (var renderer in originalRenderers)
                {
                    renderer.enabled = false;
                }

                // Create armature data - use the ROOT bone's parent as the armature root
                Transform armatureRoot = originalRenderer.rootBone != null ? originalRenderer.rootBone.parent : characterRoot;
                if (armatureRoot == null) armatureRoot = characterRoot;

                ModelChangerPlugin.Log?.LogInfo($"Using armature root: {armatureRoot.name}");
                var armatureData = new AssetLoader.ArmatureData(armatureRoot.gameObject);

                // Create parent for all custom mesh parts
                GameObject customModelParent = new GameObject(modelData.Name + "_CustomModel");

                // Parent to the ARMATURE ROOT, not the character root
                customModelParent.transform.SetParent(armatureRoot, false);
                customModelParent.transform.localPosition = Vector3.zero;
                customModelParent.transform.localRotation = Quaternion.identity;
                customModelParent.transform.localScale = Vector3.one;

                int meshIndex = 0;
                Material baseMaterial = null;
                Texture2D customTexture = null;

                // Load custom texture once if available
                if (!string.IsNullOrEmpty(modelData.TexturePath) && File.Exists(modelData.TexturePath))
                {
                    customTexture = ImageLoader.LoadTexture(modelData.TexturePath);
                    if (customTexture != null)
                    {
                        ModelChangerPlugin.Log?.LogInfo($"Loaded texture: {Path.GetFileName(modelData.TexturePath)}");
                    }
                }

                // Clone base material once
                baseMaterial = new Material(originalRenderer.sharedMaterial);
                if (customTexture != null)
                {
                    baseMaterial.mainTexture = customTexture;
                }

                foreach (var sourceMesh in meshes)
                {
                    // Build the mesh with proper bone mapping
                    var customMesh = AssetLoader.BuildMesh(sourceMesh, armatureData);
                    if (customMesh == null)
                    {
                        ModelChangerPlugin.Log?.LogWarning($"Failed to build mesh {meshIndex}: {sourceMesh.name}");
                        continue;
                    }

                    // Create GameObject for this mesh part
                    var meshPart = new GameObject($"{modelData.Name}_Part{meshIndex}");
                    meshPart.transform.SetParent(customModelParent.transform, false);
                    meshPart.transform.localPosition = Vector3.zero;
                    meshPart.transform.localRotation = Quaternion.identity;
                    meshPart.transform.localScale = Vector3.one;

                    // Set up the skinned mesh renderer
                    var skinnedRenderer = meshPart.AddComponent<SkinnedMeshRenderer>();
                    skinnedRenderer.sharedMesh = customMesh;

                    // CRITICAL: Copy exact bone references
                    skinnedRenderer.bones = originalRenderer.bones;
                    skinnedRenderer.rootBone = originalRenderer.rootBone;

                    // Use original bounds or recalculate
                    skinnedRenderer.localBounds = customMesh.bounds;

                    // Use the shared material
                    skinnedRenderer.sharedMaterial = baseMaterial;

                    ModelChangerPlugin.Log?.LogInfo($"Part {meshIndex}: {customMesh.vertexCount} verts, {customMesh.triangles.Length / 3} tris");
                    meshIndex++;
                }

                // Store reference
                ModelChangerPlugin.CurrentCharacterObject = customModelParent;

                ModelChangerPlugin.Log?.LogInfo($"Successfully loaded '{modelData.Name}' with {meshIndex} parts");
            }
            catch (System.Exception ex)
            {
                ModelChangerPlugin.Log?.LogError($"Failed to load '{modelData.Name}': {ex}");
                ModelChangerPlugin.Log?.LogError($"Stack: {ex.StackTrace}");
            }
        }
    }
}