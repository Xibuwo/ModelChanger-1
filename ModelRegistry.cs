using System.Collections.Generic;
using UnityEngine;
using BepInEx;
using System.Linq;
using System.IO;

namespace ChillWithYou.ModelChanger
{
    public class ModelData
    {
        public string Name;
        public string FBXPath;
        public string TexturePath;
        public Texture2D PreviewImage;
        public bool IsCustom;
    }

    public class ModelRegistry
    {
        private Dictionary<string, ModelData> _models = new Dictionary<string, ModelData>();

        public ModelRegistry()
        {
            // Register default game model
            RegisterModel(new ModelData
            {
                Name = "Default",
                FBXPath = null,
                TexturePath = null,
                IsCustom = false
            });

            // Load custom models from BepInEx/plugins/ModelChanger/Models/
            LoadCustomModels();
        }

        public void RegisterModel(ModelData model)
        {
            _models[model.Name] = model;
        }

        public ModelData GetModel(string name)
        {
            return _models.TryGetValue(name, out var model) ? model : null;
        }

        public IEnumerable<ModelData> GetAllModels()
        {
            return _models.Values.OrderBy(m => m.Name);
        }

        private void LoadCustomModels()
        {
            string modelsPath = System.IO.Path.Combine(
                Paths.PluginPath, "ModelChanger", "Models"
            );

            if (!System.IO.Directory.Exists(modelsPath))
            {
                System.IO.Directory.CreateDirectory(modelsPath);
                ModelChangerPlugin.Log?.LogInfo($"Created models directory: {modelsPath}");

                // Create an example folder structure
                string examplePath = Path.Combine(modelsPath, "ExampleModel");
                System.IO.Directory.CreateDirectory(examplePath);

                string readmePath = Path.Combine(examplePath, "README.txt");
                File.WriteAllText(readmePath,
                    "Place your FBX file and texture files in this folder.\n\n" +
                    "Required:\n" +
                    "- YourModel.fbx (the 3D model file)\n\n" +
                    "Optional:\n" +
                    "- YourTexture.png/jpg/tga (main texture for the model)\n" +
                    "- preview.png/jpg (thumbnail shown in the model selector)\n\n" +
                    "The folder name will be used as the model name in the menu."
                );

                ModelChangerPlugin.Log?.LogInfo($"Created example folder with README at: {examplePath}");
                return;
            }

            // Scan each subfolder in Models directory
            foreach (var folder in System.IO.Directory.GetDirectories(modelsPath))
            {
                try
                {
                    string modelName = Path.GetFileName(folder);
                    LoadModelFromFolder(folder, modelName);
                }
                catch (System.Exception ex)
                {
                    ModelChangerPlugin.Log?.LogError($"Failed to load model from folder {folder}: {ex}");
                }
            }

            ModelChangerPlugin.Log?.LogInfo($"Loaded {_models.Count - 1} custom model(s)");
        }

        private void LoadModelFromFolder(string folderPath, string modelName)
        {
            // Find FBX file
            var fbxFiles = System.IO.Directory.GetFiles(folderPath, "*.fbx");
            if (fbxFiles.Length == 0)
            {
                ModelChangerPlugin.Log?.LogWarning($"No FBX file found in folder: {folderPath}");
                return;
            }

            string fbxPath = fbxFiles[0]; // Use the first FBX file found
            if (fbxFiles.Length > 1)
            {
                ModelChangerPlugin.Log?.LogWarning($"Multiple FBX files found in {folderPath}, using: {Path.GetFileName(fbxPath)}");
            }

            // Find main texture (look for common texture file names first)
            string texturePath = FindTexture(folderPath, new[] {
                "texture", "diffuse", "albedo", "color", "main",
                Path.GetFileNameWithoutExtension(fbxPath) // Try FBX name
            });

            // Find preview image
            Texture2D previewImage = LoadPreviewImage(folderPath);

            // Register the model
            RegisterModel(new ModelData
            {
                Name = modelName,
                FBXPath = fbxPath,
                TexturePath = texturePath,
                PreviewImage = previewImage,
                IsCustom = true
            });

            ModelChangerPlugin.Log?.LogInfo($"Loaded model '{modelName}' from: {Path.GetFileName(fbxPath)}");

            if (!string.IsNullOrEmpty(texturePath))
            {
                ModelChangerPlugin.Log?.LogInfo($"  - Texture: {Path.GetFileName(texturePath)}");
            }
            else
            {
                ModelChangerPlugin.Log?.LogWarning($"  - No texture found for model '{modelName}'");
            }
        }

        private string FindTexture(string folderPath, string[] preferredNames)
        {
            string[] imageExtensions = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp", "*.dds" };

            // First, try to find textures with preferred names
            foreach (var preferredName in preferredNames)
            {
                foreach (var ext in imageExtensions)
                {
                    var files = System.IO.Directory.GetFiles(folderPath, preferredName + ext.TrimStart('*'));
                    if (files.Length > 0)
                    {
                        return files[0];
                    }
                }
            }

            // If no preferred name found, just get the first image file (excluding preview)
            foreach (var ext in imageExtensions)
            {
                var files = System.IO.Directory.GetFiles(folderPath, ext)
                    .Where(f => !Path.GetFileNameWithoutExtension(f).ToLower().Contains("preview"))
                    .ToArray();

                if (files.Length > 0)
                {
                    return files[0];
                }
            }

            return null;
        }

        private Texture2D LoadPreviewImage(string folderPath)
        {
            string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };

            // Look for files with "preview" in the name
            foreach (var ext in imageExtensions)
            {
                string previewPath = Path.Combine(folderPath, "preview" + ext);
                if (File.Exists(previewPath))
                {
                    try
                    {
                        return ImageLoader.LoadTexture(previewPath);
                    }
                    catch (System.Exception ex)
                    {
                        ModelChangerPlugin.Log?.LogWarning($"Failed to load preview image {previewPath}: {ex.Message}");
                    }
                }
            }

            return null;
        }
    }
}