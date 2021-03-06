﻿using B83.Image.BMP;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace MafiaUnity
{
    public class ModelGenerator : BaseGenerator
    {
        public static Dictionary<string, Texture2D> cachedTextures = new Dictionary<string, Texture2D>();

        public override GameObject LoadObject(string path, Mission mission)
        {
            GameObject rootObject = LoadCachedObject(path);
            
            if (rootObject == null)
                rootObject = new GameObject(path);
            else
                return rootObject;

            Stream fs;

            try
            {
                fs = GameAPI.instance.fileSystem.GetStreamFromPath(path);
            }
            catch (Exception ex)
            {
                GameObject.DestroyImmediate(rootObject);
                Debug.LogWarning(ex.ToString());
                return null;
            }

            using (BinaryReader reader = new BinaryReader(fs))
            {
                var modelLoader = new MafiaFormats.Reader4DS();
                var model = modelLoader.loadModel(reader);
                fs.Close();

                if (model == null)
                    return null;

                var meshId = 0;

                var children = new List<KeyValuePair<int, Transform>>();

                foreach (var mafiaMesh in model.meshes)
                {
                    var child = new GameObject(mafiaMesh.meshName, typeof(MeshFilter));
                    var meshFilter = child.GetComponent<MeshFilter>();

                    StoreReference(mission, child.name, child);
                    
                    children.Add(new KeyValuePair<int, Transform>(mafiaMesh.parentID, child.transform));

                    if (mafiaMesh.meshType == MafiaFormats.MeshType.Joint)
                    {
                        var bone = child.AddComponent<Bone>();
                        bone.data = mafiaMesh.joint;
                        continue;
                    }
                    else if (mafiaMesh.meshType == MafiaFormats.MeshType.Collision)
                    {
                        Material[] temp;
                        child.AddComponent<MeshCollider>().sharedMesh = GenerateMesh(mafiaMesh, child, mafiaMesh.standard.lods[0], model, out temp);
                        continue;
                    }
                    else if (mafiaMesh.meshType == MafiaFormats.MeshType.Sector)
                    {
                        // NOTE: Set up dummy data for this sector.
                        MafiaFormats.Scene2BINLoader.Object dummySectorData = new MafiaFormats.Scene2BINLoader.Object();
                        dummySectorData.type = MafiaFormats.Scene2BINLoader.ObjectType.Sector;
                        var objDef = child.AddComponent<ObjectDefinition>();
                        objDef.data = dummySectorData;
                        objDef.sectorBounds.SetMinMax(mafiaMesh.sector.minBox, mafiaMesh.sector.maxBox);
                        objDef.Init();
                        continue;
                    }
                    else if (mafiaMesh.meshType != MafiaFormats.MeshType.Standard)
                        continue;

                    if (mafiaMesh.standard.instanced != 0)
                        continue;

                    var def = child.AddComponent<ModelDefinition>();
                    def.model = model;
                    def.mesh = mafiaMesh;
                    
                    Material[] materials;

                    switch (mafiaMesh.visualMeshType)
                    {
                        case MafiaFormats.VisualMeshType.Standard:
                        {
                            // TODO build up more lods
                            if (mafiaMesh.standard.lods.Count > 0)
                            {
                                var meshRenderer = child.AddComponent<MeshRenderer>();
                                meshFilter.mesh = GenerateMesh(mafiaMesh, child, mafiaMesh.standard.lods[0], model, out materials);
                                meshRenderer.materials = materials;
                                meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;

                                // Handle special textures
                                foreach (var m in meshRenderer.sharedMaterials)
                                {
                                    var name = m.GetTexture("_MainTex")?.name;

                                    if (IsTextureGlow(name))
                                    {
                                        var glowTexture = (Texture2D)Resources.Load("Flares/" + Path.GetFileNameWithoutExtension(name));

                                        m.shader = Shader.Find("Unlit/Transparent");
                                        m.SetTexture("_MainTex", glowTexture);
                                        
                                        break;
                                    }
                                }
                            }
                            else
                                continue;
                        }
                        break;

                        case MafiaFormats.VisualMeshType.Single_Mesh:
                        {
                            var meshRenderer = child.AddComponent<SkinnedMeshRenderer>();
                            meshFilter.mesh = GenerateMesh(mafiaMesh, child, mafiaMesh.singleMesh.standard.lods[0], model, out materials);
                            meshRenderer.materials = materials;
                            meshRenderer.sharedMesh = meshFilter.sharedMesh;
                            meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                        }
                        break;

                        case MafiaFormats.VisualMeshType.Single_Morph:
                        {
                            var meshRenderer = child.AddComponent<SkinnedMeshRenderer>();
                            meshFilter.mesh = GenerateMesh(mafiaMesh, child, mafiaMesh.singleMorph.singleMesh.standard.lods[0], model, out materials);
                            meshRenderer.materials = materials;
                            meshRenderer.sharedMesh = meshFilter.sharedMesh;
                            meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                        }
                        break;

                        case MafiaFormats.VisualMeshType.Billboard:
                        {
                            // TODO build up more lods
                            var standard = mafiaMesh.billboard.standard;
                            
                            if (standard.lods.Count > 0)
                            {
                                //NOTE: (DavoSK) Add our custom billboard here
                                child.AddComponent<CustomBillboard>();

                                var meshRenderer = child.AddComponent<MeshRenderer>();

                                meshFilter.mesh = GenerateMesh(mafiaMesh, child, standard.lods[0], model, out materials);
                                meshRenderer.materials = materials;
                                meshRenderer.shadowCastingMode = ShadowCastingMode.TwoSided;

                                // Handle special textures
                                foreach (var m in meshRenderer.sharedMaterials)
                                {
                                    var name = m.GetTexture("_MainTex")?.name;

                                    if (IsTextureGlow(name))
                                    {
                                        var glowTexture = (Texture2D)Resources.Load("Flares/" + Path.GetFileNameWithoutExtension(name));

                                        m.shader = Shader.Find("Unlit/Transparent");
                                        m.SetTexture("_MainTex", glowTexture);

                                        break;
                                    }
                                }
                            }
                            else
                                continue;
                        }
                        break;

                        case MafiaFormats.VisualMeshType.Glow:
                        {
                            List<string> usedMaps = new List<string>();

                            foreach (var g in mafiaMesh.glow.glowData)
                            {
                                if (g.materialID-1 >= model.materials.Count)
                                    continue;

                                var matID = g.materialID-1;

                                var mat = model.materials[matID];
                                var mapName = mat.diffuseMapName;

                                if (usedMaps.Contains(mapName))
                                    continue;

                                foreach (var m in model.meshes)
                                {
                                    if (m.standard.lods == null)
                                        continue;

                                    if (m.standard.lods.Count < 1)
                                        continue;

                                    bool used = false;

                                    foreach (var gr in m.standard.lods[0].faceGroups)
                                    {
                                        if (gr.materialID == matID)
                                        {
                                            GenerateGlow(mapName, rootObject, m.pos);

                                            used = true;
                                            break;
                                        }
                                    }

                                    if (used == true)
                                        break;
                                }

                                usedMaps.Add(mapName);
                            }
                        }
                        break;

                        // TODO add more visual types

                        default: continue;
                    }

                    def.modelName = Path.GetFileName(path);
                    
                    meshId++;
                }
                
                for (int i = 0; i < children.Count; i++)
                {
                    var parentId = children[i].Key;
                    var mafiaMesh = model.meshes[i];

                    if (parentId > 0)
                        children[i].Value.parent = children[parentId - 1].Value;
                    else
                        children[i].Value.parent = rootObject.transform;

                    children[i].Value.localPosition = mafiaMesh.pos;
                    children[i].Value.localRotation = mafiaMesh.rot;
                    children[i].Value.localScale = mafiaMesh.scale;
                }

                // NOTE(zaklaus): Do some extra work if this is a skinned mesh
                var baseObject = rootObject.transform.Find("base");

                if (baseObject != null)
                {
                    var skinnedMesh = baseObject.GetComponent<SkinnedMeshRenderer>();

                    if (skinnedMesh != null)
                    {
                        var def = baseObject.GetComponent<ModelDefinition>();
                        MafiaFormats.SingleMesh data;

                        if (def.mesh.visualMeshType == MafiaFormats.VisualMeshType.Single_Mesh)
                            data = def.mesh.singleMesh;
                        else
                            data = def.mesh.singleMorph.singleMesh;

                        var boneData = data.LODs[0];
                        var bones = new List<Bone>(skinnedMesh.GetComponentsInChildren<Bone>());
                        var boneArray = new Transform[bones.Count];
                        
                        foreach (var b in bones)
                        {
                            boneArray[b.data.boneID] = b.transform;
                        }
                        
                        /* TODO: var boneTransforms = new List<Transform>(boneArray); */
                        var bindPoses = new Matrix4x4[bones.Count];
                        var boneWeights = new BoneWeight[skinnedMesh.sharedMesh.vertexCount];

                        skinnedMesh.bones = boneArray;
                        
                        int skipVertices = 0;//(int)boneData.nonWeightedVertCount;
                        
                        for (int i = 0; i < boneData.bones.Count; i++)
                        {
                            bindPoses[i] = boneData.bones[i].transform;

                            for (int j = 0; j < boneData.bones[i].oneWeightedVertCount; j++)
                            {
                                boneWeights[skipVertices + j].boneIndex0 = i;
                                boneWeights[skipVertices + j].weight0 = 1f;
                            }

                            skipVertices += (int)boneData.bones[i].oneWeightedVertCount;

                            for (int j = 0; j < boneData.bones[i].weights.Count; j++)
                            {
                                boneWeights[skipVertices + j].boneIndex0 = i;
                                boneWeights[skipVertices + j].weight0 = boneData.bones[i].weights[j];
                                boneWeights[skipVertices + j].boneIndex1 = (int)boneData.bones[i].boneID;
                                boneWeights[skipVertices + j].weight1 = 1f - boneData.bones[i].weights[j]; 
                            }

                            skipVertices += boneData.bones[i].weights.Count;

                        }

                        skinnedMesh.sharedMesh.bindposes = bindPoses;
                        skinnedMesh.sharedMesh.boneWeights = boneWeights;
                    }
                }

                children.Clear();
            }
            
            StoreChachedObject(path, rootObject);

            return rootObject;
        }

        Mesh GenerateMesh(MafiaFormats.Mesh mafiaMesh, GameObject ent, MafiaFormats.LOD firstMafiaLOD, MafiaFormats.Model model, out Material[] materials)
        {
            var mesh = new Mesh();
            
            List<Material> mats = new List<Material>();

            List<Vector3> unityVerts = new List<Vector3>();
            List<Vector3> unityNormals = new List<Vector3>();
            List<Vector2> unityUV = new List<Vector2>();

            foreach (var vert in firstMafiaLOD.vertices)
            {
                unityVerts.Add(vert.pos);
                unityNormals.Add(vert.normal);
                unityUV.Add(new Vector2(vert.uv.x, -1 * vert.uv.y));
            }
            
            mesh.name = mafiaMesh.meshName;

            mesh.SetVertices(unityVerts);
            mesh.SetUVs(0, unityUV);
            mesh.SetNormals(unityNormals);

            mesh.subMeshCount = firstMafiaLOD.faceGroups.Count;

            var faceGroupId = 0;

            foreach (var faceGroup in firstMafiaLOD.faceGroups)
            {
                List<int> unityIndices = new List<int>();
                foreach (var face in faceGroup.faces)
                {
                    unityIndices.Add(face.a);
                    unityIndices.Add(face.b);
                    unityIndices.Add(face.c);
                }

                mesh.SetTriangles(unityIndices.ToArray(), faceGroupId);

                var matId = (int)Mathf.Max(0, Mathf.Min(model.materials.Count - 1, faceGroup.materialID - 1));

                if (model.materials.Count > 0)
                {
                    var mafiaMat = model.materials[matId];

                    Material mat;

                    if (mafiaMat.flags.HasFlag(MafiaFormats.MaterialFlag.Colorkey))
                    {
                        //mat = new Material(Shader.Find("Legacy Shaders/Transparent/Cutout/Diffuse"));
                        mat = new Material(Shader.Find("Mafia/Cutout"));
                        mat.SetFloat("_Cutoff", 0.9f);
                    }
                    else if (mafiaMat.transparency < 1f)
                    {
                        mat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
                        mat.renderQueue = 2005;
                    }
                    else if (mafiaMat.flags.HasFlag(MafiaFormats.MaterialFlag.AlphaTexture))
                    {
                        mat = new Material(Shader.Find("Legacy Shaders/Transparent/Diffuse"));
                        mat.renderQueue = 2005;
                    }
                    else
                    {
                        mat = new Material(Shader.Find("Mafia/Diffuse"));
                    }

                    if ((mafiaMat.diffuseMapName != null && mafiaMat.diffuseMapName.Trim().Length > 0) ||
                        (mafiaMat.alphaMapName != null && mafiaMat.alphaMapName.Trim().Length > 0))
                    {
                        if ((mafiaMat.flags & MafiaFormats.MaterialFlag.Colorkey) != 0)
                            bmp.useTransparencyKey = true;

                        bool useColorKey = mafiaMat.flags.HasFlag(MafiaFormats.MaterialFlag.Colorkey);

                        Texture2D tex = LoadTexture(mafiaMat.diffuseMapName, useColorKey, mafiaMat.transparency < 1f);
                        Texture2D alphaTex = LoadTexture(mafiaMat.alphaMapName, useColorKey, true);
                        
                        if (tex != null)
                        {
                            mat.SetTexture("_MainTex", tex);

                            if (GameAPI.instance.cvarManager.Get("filterMode", "1") == "0")
                                tex.filterMode = FilterMode.Point;
                        }

                        if (alphaTex != null && mafiaMat.flags.HasFlag(MafiaFormats.MaterialFlag.AlphaTexture))
                        {
                            mat.SetTexture("_MainTex", alphaTex);
                        }

                        if (mafiaMat.flags.HasFlag(MafiaFormats.MaterialFlag.Animated_Texture_Diffuse) || mafiaMat.flags.HasFlag(MafiaFormats.MaterialFlag.Animated_Texture_Alpha))
                        {
                            List<Texture2D> frames = new List<Texture2D>();

                            string fileName = null;

                            if ((mafiaMat.flags & MafiaFormats.MaterialFlag.Animated_Texture_Diffuse) != 0)
                                fileName = mafiaMat.diffuseMapName;
                            else
                                fileName = mafiaMat.alphaMapName;

                            if ((mafiaMat.flags & MafiaFormats.MaterialFlag.Colorkey) != 0)
                                bmp.useTransparencyKey = true;

                            if (fileName != null && fileName.Trim().Length > 0)
                            {
                                var path = fileName.Split('.');
                                string baseName = path[0];
                                string ext = path[1];

                                baseName = baseName.Substring(0, baseName.Length - 2);

                                for (int k = 0; k < mafiaMat.animSequenceLength; k++)
                                {
                                    try
                                    {
                                        var animPath = Path.Combine("maps", baseName + k.ToString("D2") + "." + ext);
                                        var frameImage = bmp.LoadBMP(GameAPI.instance.fileSystem.GetStreamFromPath(animPath));

                                        if (frameImage == null)
                                            continue;

                                        var frame = frameImage.ToTexture2D();
                                        frames.Add(frame);
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogError(ex.ToString());
                                    }
                                }

                                var framePlayer = ent.AddComponent<TextureAnimationPlayer>();

                                framePlayer.frames = frames;
                                framePlayer.framePeriod = mafiaMat.framePeriod;
                                framePlayer.material = mat;
                            }

                            bmp.useTransparencyKey = false;
                        }
                    }
                    
                    mats.Add(mat);
                }

                faceGroupId++;
            }

            materials = mats.ToArray();

            return mesh;
        }

        public static Texture2D LoadTexture(string name, bool useColorKey, bool alphaFromGrayscale=false, bool ignoreCachedTexture=false)
        {
            Texture2D tex = null;

            if (name == null)
                return null;

            var modMapName = GameAPI.instance.fileSystem.GetPath(Path.Combine("maps", name));

            if (cachedTextures.ContainsKey(modMapName) && ignoreCachedTexture == false
                && cachedTextures[modMapName] != null)
            {
                tex = cachedTextures[modMapName];
            }
            else
            {
                BMPImage image = null;

                bmp.useTransparencyKey = useColorKey;

                try
                {
                    image = bmp.LoadBMP(GameAPI.instance.fileSystem.GetStreamFromPath(Path.Combine("maps", name)));
                }
                catch
                {
                    Debug.LogWarningFormat("Image {0} couldn't be loaded!", name);
                }

                if (image != null)
                {
                    tex = image.ToTexture2D();
                    tex.name = name;


                    if (alphaFromGrayscale)
                    {
                        var data = tex.GetPixels();

                        for (int i = 0; i < data.Length; i++)
                        {
                            var p = data[i];
                            data[i].a = p.grayscale;
                        }

                        tex.SetPixels(data, 0);
                        tex.Apply();
                    }
                }

                if (ignoreCachedTexture == false)
                {
                    if (cachedTextures.ContainsKey(modMapName))
                        cachedTextures.Remove(modMapName);

                    cachedTextures.Add(modMapName, tex);
                }

                bmp.useTransparencyKey = false;
            }

            return tex;
        }

        public static void GenerateGlow(string mapName, GameObject rootObject, Vector3 pos)
        {
            var flareObject = new GameObject("Flare " + mapName);
            flareObject.transform.parent = rootObject.transform;
            flareObject.transform.localPosition = pos;

            string glowName = Path.GetFileNameWithoutExtension(mapName);

            var glow = flareObject.AddComponent<LensFlare>();
            flareObject.AddComponent<LensFlareFixedDistance>();

            var flarePrefab = (Flare)Resources.Load("Flares/" + glowName + "_FLARE");

            if (flarePrefab == null)
            {
                Debug.LogWarningFormat("Flare {0} couldn't be found! Using 00GLOW instead!", glowName);
                
                flarePrefab = (Flare)Resources.Load("Flares/00GLOW_FLARE");
            }

            var flare = (Flare)GameObject.Instantiate(flarePrefab);
            glow.flare = flare;
            glow.fadeSpeed = 8f;
            glow.brightness = 2f;
        }

        public static bool IsTextureGlow(string mapName)
        {
            string glowName = Path.GetFileNameWithoutExtension(mapName);

            return glowNames.Contains(glowName);
        }

        static List<string> glowNames = new List<string> {
            "00GLOW",
            "2CLGL+",
            "2CBGL+",
        };

        static BMPLoader bmp = new BMPLoader();
    }
}