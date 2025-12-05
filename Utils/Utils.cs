using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Assimp;

namespace ChillWithYou.ModelChanger
{
    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D tex, byte[] data)
        {
            return tex.LoadImage(data);
        }
    }

    public static class ImageLoader
    {
        public static Texture2D LoadTexture(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            byte[] fileData = File.ReadAllBytes(filePath);
            Texture2D tex = new Texture2D(2, 2);

            try
            {
                ImageConversion.LoadImage(tex, fileData);
                return tex;
            }
            catch
            {
                ModelChangerPlugin.Log?.LogWarning($"Failed to load texture: {filePath}");
                return null;
            }
        }
    }

    public static class AssetLoader
    {
        private static Dictionary<UnityEngine.Mesh, List<string>> _meshBoneNames = new Dictionary<UnityEngine.Mesh, List<string>>();

        public class ArmatureData
        {
            public Transform Root;
            public Dictionary<string, Transform> BoneMap = new Dictionary<string, Transform>();
            public List<string> OriginalBoneOrder = new List<string>();

            public ArmatureData(GameObject rootObject)
            {
                Root = rootObject.transform;
                MapHierarchy(Root);

                // Get bone order from existing renderer
                var skinnedRenderer = rootObject.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skinnedRenderer != null && skinnedRenderer.bones != null)
                {
                    foreach (var bone in skinnedRenderer.bones)
                    {
                        if (bone != null)
                        {
                            OriginalBoneOrder.Add(bone.name);
                        }
                    }
                    ModelChangerPlugin.Log?.LogInfo($"Captured {OriginalBoneOrder.Count} bones from game skeleton");
                }
            }

            private void MapHierarchy(Transform current)
            {
                // Use case-insensitive matching for bone names
                string boneName = current.name.ToLower();
                if (!BoneMap.ContainsKey(boneName))
                {
                    BoneMap[boneName] = current;
                }

                foreach (Transform child in current)
                {
                    MapHierarchy(child);
                }
            }

            public Transform FindBone(string boneName)
            {
                // Try exact match first
                if (BoneMap.TryGetValue(boneName, out var bone))
                    return bone;

                // Try case-insensitive
                string lowerName = boneName.ToLower();
                if (BoneMap.TryGetValue(lowerName, out bone))
                    return bone;

                // Try partial match (e.g., "mixamorig:Hips" -> "Hips")
                foreach (var kvp in BoneMap)
                {
                    if (kvp.Key.EndsWith(lowerName) || lowerName.EndsWith(kvp.Key))
                        return kvp.Value;
                }

                return null;
            }
        }

        public static UnityEngine.Mesh[] LoadFBX(string path)
        {
            if (!File.Exists(path))
            {
                ModelChangerPlugin.Log?.LogError($"FBX not found: {path}");
                return null;
            }

            ModelChangerPlugin.Log?.LogInfo($"Loading FBX: {path}");

            using (var importer = new AssimpContext())
            {
                var config = PostProcessSteps.Triangulate |
                             PostProcessSteps.LimitBoneWeights |
                             PostProcessSteps.GenerateSmoothNormals |
                             PostProcessSteps.FlipWindingOrder |
                             PostProcessSteps.MakeLeftHanded;

                Scene scene;
                try
                {
                    scene = importer.ImportFile(path, config);
                }
                catch (System.Exception ex)
                {
                    ModelChangerPlugin.Log?.LogError($"Assimp import failed: {ex.Message}");
                    return null;
                }

                if (scene == null || !scene.HasMeshes)
                {
                    ModelChangerPlugin.Log?.LogError("Scene has no meshes");
                    return null;
                }

                List<UnityEngine.Mesh> resultMeshes = new List<UnityEngine.Mesh>();

                foreach (var aMesh in scene.Meshes)
                {
                    UnityEngine.Mesh uMesh = new UnityEngine.Mesh();
                    uMesh.name = aMesh.Name ?? "ImportedMesh";

                    // Vertices
                    if (aMesh.HasVertices)
                        uMesh.vertices = aMesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray();

                    // Normals
                    if (aMesh.HasNormals)
                        uMesh.normals = aMesh.Normals.Select(n => new Vector3(n.X, n.Y, n.Z)).ToArray();

                    // UVs
                    if (aMesh.HasTextureCoords(0))
                    {
                        var textureCoords = aMesh.TextureCoordinateChannels[0];
                        uMesh.uv = textureCoords.Select(uv => new Vector2(uv.X, uv.Y)).ToArray();
                    }

                    // Triangles
                    if (aMesh.HasFaces)
                    {
                        List<int> indices = new List<int>();
                        foreach (var face in aMesh.Faces)
                            if (face.IndexCount == 3)
                                indices.AddRange(face.Indices);
                        uMesh.SetIndices(indices.ToArray(), MeshTopology.Triangles, 0);
                    }

                    // Bones & Weights
                    if (aMesh.HasBones)
                    {
                        List<BoneWeight> weights = new List<BoneWeight>(new BoneWeight[uMesh.vertexCount]);
                        List<string> boneNamesForThisMesh = new List<string>();
                        List<UnityEngine.Matrix4x4> bindPoses = new List<UnityEngine.Matrix4x4>();

                        for (int i = 0; i < aMesh.Bones.Count; i++)
                        {
                            var bone = aMesh.Bones[i];
                            string boneName = bone.Name;

                            // Clean up bone names (remove prefixes like "mixamorig:")
                            if (boneName.Contains(":"))
                                boneName = boneName.Split(':').Last();

                            boneNamesForThisMesh.Add(boneName);

                            // Convert bind pose matrix
                            Assimp.Matrix4x4 m = bone.OffsetMatrix;
                            bindPoses.Add(new UnityEngine.Matrix4x4(
                                new Vector4(m.A1, m.B1, m.C1, m.D1),
                                new Vector4(m.A2, m.B2, m.C2, m.D2),
                                new Vector4(m.A3, m.B3, m.C3, m.D3),
                                new Vector4(m.A4, m.B4, m.C4, m.D4)));

                            // Apply weights
                            foreach (var w in bone.VertexWeights)
                            {
                                if (w.VertexID >= weights.Count) continue;

                                BoneWeight bw = weights[w.VertexID];
                                if (bw.weight0 == 0) { bw.boneIndex0 = i; bw.weight0 = w.Weight; }
                                else if (bw.weight1 == 0) { bw.boneIndex1 = i; bw.weight1 = w.Weight; }
                                else if (bw.weight2 == 0) { bw.boneIndex2 = i; bw.weight2 = w.Weight; }
                                else if (bw.weight3 == 0) { bw.boneIndex3 = i; bw.weight3 = w.Weight; }

                                weights[w.VertexID] = bw;
                            }
                        }

                        uMesh.boneWeights = weights.ToArray();
                        uMesh.bindposes = bindPoses.ToArray();

                        if (_meshBoneNames.ContainsKey(uMesh))
                            _meshBoneNames.Remove(uMesh);
                        _meshBoneNames.Add(uMesh, boneNamesForThisMesh);

                        ModelChangerPlugin.Log?.LogInfo($"Mesh '{uMesh.name}' has {boneNamesForThisMesh.Count} bones");
                    }
                    else
                    {
                        ModelChangerPlugin.Log?.LogWarning($"Mesh '{uMesh.name}' has NO bone data - will be static");
                    }

                    uMesh.RecalculateBounds();
                    resultMeshes.Add(uMesh);
                }

                return resultMeshes.ToArray();
            }
        }

        public static UnityEngine.Mesh BuildMesh(UnityEngine.Mesh sourceMesh, ArmatureData armatureData)
        {
            if (sourceMesh == null) return null;

            // Check if this mesh has bone data
            if (!_meshBoneNames.TryGetValue(sourceMesh, out var importedBoneNames))
            {
                ModelChangerPlugin.Log?.LogWarning($"Mesh '{sourceMesh.name}' has no bone mapping - returning static mesh");
                var staticMesh = Object.Instantiate(sourceMesh);
                staticMesh.boneWeights = null;
                staticMesh.bindposes = null;
                return staticMesh;
            }

            ModelChangerPlugin.Log?.LogInfo($"Mapping {importedBoneNames.Count} bones from '{sourceMesh.name}'");

            UnityEngine.Mesh newMesh = Object.Instantiate(sourceMesh);
            var originalWeights = newMesh.boneWeights;
            var newWeights = new BoneWeight[originalWeights.Length];

            // Create bone index remap
            int[] boneIndexRemap = new int[importedBoneNames.Count];
            int unmappedBones = 0;

            for (int i = 0; i < importedBoneNames.Count; i++)
            {
                string fbxBoneName = importedBoneNames[i];
                int targetIndex = -1;

                // Try to find this bone in the game's skeleton
                for (int j = 0; j < armatureData.OriginalBoneOrder.Count; j++)
                {
                    string gameBoneName = armatureData.OriginalBoneOrder[j];

                    // Try various matching strategies
                    if (gameBoneName.Equals(fbxBoneName, System.StringComparison.OrdinalIgnoreCase) ||
                        gameBoneName.ToLower().Contains(fbxBoneName.ToLower()) ||
                        fbxBoneName.ToLower().Contains(gameBoneName.ToLower()))
                    {
                        targetIndex = j;
                        break;
                    }
                }

                if (targetIndex == -1)
                {
                    // Default to root bone to prevent complete breakage
                    targetIndex = 0;
                    unmappedBones++;
                    if (unmappedBones <= 5)  // Only log first few to avoid spam
                    {
                        ModelChangerPlugin.Log?.LogWarning($"  Could not map bone '{fbxBoneName}' -> defaulting to root");
                    }
                }

                boneIndexRemap[i] = targetIndex;
            }

            if (unmappedBones > 0)
            {
                ModelChangerPlugin.Log?.LogWarning($"Total unmapped bones: {unmappedBones}/{importedBoneNames.Count}");
            }

            // Remap vertex weights
            for (int i = 0; i < originalWeights.Length; i++)
            {
                var ow = originalWeights[i];
                var nw = new BoneWeight
                {
                    weight0 = ow.weight0,
                    boneIndex0 = boneIndexRemap[ow.boneIndex0],
                    weight1 = ow.weight1,
                    boneIndex1 = boneIndexRemap[ow.boneIndex1],
                    weight2 = ow.weight2,
                    boneIndex2 = boneIndexRemap[ow.boneIndex2],
                    weight3 = ow.weight3,
                    boneIndex3 = boneIndexRemap[ow.boneIndex3]
                };
                newWeights[i] = nw;
            }

            newMesh.boneWeights = newWeights;
            _meshBoneNames.Remove(sourceMesh);

            return newMesh;
        }
    }
}