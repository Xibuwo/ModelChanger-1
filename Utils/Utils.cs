using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Assimp; // Requires AssimpNet.dll

namespace ChillWithYou.ModelChanger
{
    public static class ImageConversion
    {
        public static bool LoadImage(Texture2D tex, byte[] data)
        {
            // Unity 2018+ has ImageConversion.LoadImage as a static method
            // For Unity 2022, it's in UnityEngine.ImageConversion
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

            // Try newer Unity method first (2018+)
            try
            {
                ImageConversion.LoadImage(tex, fileData);
                return tex;
            }
            catch
            {
                // Fallback: manual loading for older Unity
                // This is a basic fallback - may not work for all formats
                ModelChangerPlugin.Log?.LogWarning($"Failed to load texture: {filePath}");
                return null;
            }
        }
    }

    public static class AssetLoader
    {
        // Sidecar to store bone names since Unity Mesh doesn't strictly preserve them in a way we can query easily later
        private static Dictionary<UnityEngine.Mesh, List<string>> _meshBoneNames = new Dictionary<UnityEngine.Mesh, List<string>>();

        public class ArmatureData
        {
            public Transform Root;
            // Maps BoneName -> Transform (e.g. "Hips" -> TransformObject)
            public Dictionary<string, Transform> BoneMap = new Dictionary<string, Transform>();
            // Stores the exact order of bones in the original renderer to ensure indices match
            public List<string> OriginalBoneOrder = new List<string>();

            public ArmatureData(GameObject rootObject)
            {
                Root = rootObject.transform;
                MapHierarchy(Root);

                // Attempt to capture the original bone order from the existing renderer
                var skinnedRenderer = rootObject.GetComponentInChildren<SkinnedMeshRenderer>();
                if (skinnedRenderer != null)
                {
                    foreach (var bone in skinnedRenderer.bones)
                    {
                        OriginalBoneOrder.Add(bone.name);
                    }
                }
            }

            private void MapHierarchy(Transform current)
            {
                if (!BoneMap.ContainsKey(current.name)) BoneMap[current.name] = current;
                foreach (Transform child in current) MapHierarchy(child);
            }
        }

        public static UnityEngine.Mesh[] LoadFBX(string path)
        {
            if (!File.Exists(path))
            {
                ModelChangerPlugin.Log?.LogError($"FBX file not found: {path}");
                return null;
            }

            ModelChangerPlugin.Log?.LogInfo($"Attempting to load FBX: {path}");

            using (var importer = new AssimpContext())
            {
                // Critical: Settings to match Unity's coordinate system
                var config = PostProcessSteps.Triangulate |
                             PostProcessSteps.LimitBoneWeights | // Unity supports max 4 weights per vertex
                             PostProcessSteps.GenerateSmoothNormals |
                             PostProcessSteps.FlipWindingOrder |
                             PostProcessSteps.MakeLeftHanded;

                Scene scene;
                try
                {
                    scene = importer.ImportFile(path, config);
                    ModelChangerPlugin.Log?.LogInfo($"Assimp import successful. HasMeshes: {scene?.HasMeshes}");
                }
                catch (System.Exception ex)
                {
                    ModelChangerPlugin.Log?.LogError($"Assimp import failed: {ex.Message}");
                    ModelChangerPlugin.Log?.LogError($"Stack trace: {ex.StackTrace}");
                    return null;
                }

                if (scene == null || !scene.HasMeshes)
                {
                    ModelChangerPlugin.Log?.LogError($"Scene is null or has no meshes");
                    return null;
                }

                ModelChangerPlugin.Log?.LogInfo($"Processing {scene.Meshes.Count} meshes from FBX");

                List<UnityEngine.Mesh> resultMeshes = new List<UnityEngine.Mesh>();

                foreach (var aMesh in scene.Meshes)
                {
                    UnityEngine.Mesh uMesh = new UnityEngine.Mesh();
                    uMesh.name = aMesh.Name ?? "ImportedMesh";

                    // 1. Vertices
                    if (aMesh.HasVertices)
                        uMesh.vertices = aMesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z)).ToArray();

                    // 2. Normals
                    if (aMesh.HasNormals)
                        uMesh.normals = aMesh.Normals.Select(n => new Vector3(n.X, n.Y, n.Z)).ToArray();

                    // 3. UVs - Fixed method call
                    if (aMesh.HasTextureCoords(0))
                    {
                        var textureCoords = aMesh.TextureCoordinateChannels[0];
                        uMesh.uv = textureCoords.Select(uv => new Vector2(uv.X, uv.Y)).ToArray();
                    }

                    // 4. Triangles
                    if (aMesh.HasFaces)
                    {
                        List<int> indices = new List<int>();
                        foreach (var face in aMesh.Faces)
                            if (face.IndexCount == 3) indices.AddRange(face.Indices);
                        uMesh.SetIndices(indices.ToArray(), MeshTopology.Triangles, 0);
                    }

                    // 5. Bones & Weights
                    if (aMesh.HasBones)
                    {
                        List<BoneWeight> weights = new List<BoneWeight>(new BoneWeight[uMesh.vertexCount]);
                        List<string> boneNamesForThisMesh = new List<string>();
                        List<UnityEngine.Matrix4x4> bindPoses = new List<UnityEngine.Matrix4x4>();

                        for (int i = 0; i < aMesh.Bones.Count; i++)
                        {
                            var bone = aMesh.Bones[i];
                            boneNamesForThisMesh.Add(bone.Name);

                            // Convert Assimp Matrix to Unity Matrix - Fixed namespace conflict
                            Assimp.Matrix4x4 m = bone.OffsetMatrix;
                            bindPoses.Add(new UnityEngine.Matrix4x4(
                                new Vector4(m.A1, m.B1, m.C1, m.D1),
                                new Vector4(m.A2, m.B2, m.C2, m.D2),
                                new Vector4(m.A3, m.B3, m.C3, m.D3),
                                new Vector4(m.A4, m.B4, m.C4, m.D4)));

                            // Apply weights to vertices
                            foreach (var w in bone.VertexWeights)
                            {
                                if (w.VertexID >= weights.Count) continue;

                                BoneWeight bw = weights[w.VertexID];

                                // Simple logic to fill the next available slot (Unity allows 4)
                                if (bw.weight0 == 0) { bw.boneIndex0 = i; bw.weight0 = w.Weight; }
                                else if (bw.weight1 == 0) { bw.boneIndex1 = i; bw.weight1 = w.Weight; }
                                else if (bw.weight2 == 0) { bw.boneIndex2 = i; bw.weight2 = w.Weight; }
                                else if (bw.weight3 == 0) { bw.boneIndex3 = i; bw.weight3 = w.Weight; }

                                weights[w.VertexID] = bw;
                            }
                        }

                        uMesh.boneWeights = weights.ToArray();
                        uMesh.bindposes = bindPoses.ToArray();

                        // Register bone names for later re-mapping
                        if (_meshBoneNames.ContainsKey(uMesh)) _meshBoneNames.Remove(uMesh);
                        _meshBoneNames.Add(uMesh, boneNamesForThisMesh);
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

            // Retrieve the bone names from the imported file
            if (!_meshBoneNames.TryGetValue(sourceMesh, out var importedBoneNames))
            {
                ModelChangerPlugin.Log?.LogWarning($"No bone map found for {sourceMesh.name}. Returning static mesh.");
                return Object.Instantiate(sourceMesh);
            }

            UnityEngine.Mesh newMesh = Object.Instantiate(sourceMesh);
            var originalWeights = newMesh.boneWeights;
            var newWeights = new BoneWeight[originalWeights.Length];

            // We need to map: ImportedBoneIndex -> Name -> OriginalGameBoneIndex
            int[] boneIndexRemap = new int[importedBoneNames.Count];

            for (int i = 0; i < importedBoneNames.Count; i++)
            {
                string boneName = importedBoneNames[i];

                // Find index of this bone in the Original Game Skeleton
                int targetIndex = armatureData.OriginalBoneOrder.IndexOf(boneName);

                if (targetIndex == -1)
                {
                    // Fallback: Try to find loosely by name in the map, but we really need the index 
                    // relative to the SkinnedMeshRenderer.bones array.
                    // If -1, this vertex is weighted to a bone the game doesn't have.
                    boneIndexRemap[i] = 0; // Default to root/hips to prevent artifacts
                }
                else
                {
                    boneIndexRemap[i] = targetIndex;
                }
            }

            // Remap every vertex weight
            for (int i = 0; i < originalWeights.Length; i++)
            {
                var ow = originalWeights[i];
                var nw = new BoneWeight();

                nw.weight0 = ow.weight0;
                nw.boneIndex0 = boneIndexRemap[ow.boneIndex0];

                nw.weight1 = ow.weight1;
                nw.boneIndex1 = boneIndexRemap[ow.boneIndex1];

                nw.weight2 = ow.weight2;
                nw.boneIndex2 = boneIndexRemap[ow.boneIndex2];

                nw.weight3 = ow.weight3;
                nw.boneIndex3 = boneIndexRemap[ow.boneIndex3];

                newWeights[i] = nw;
            }

            newMesh.boneWeights = newWeights;

            // Clean up sidecar
            _meshBoneNames.Remove(sourceMesh);

            return newMesh;
        }
    }
}