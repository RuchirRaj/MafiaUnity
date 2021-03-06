﻿using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MafiaUnity
{
    public class CityGenerator : BaseGenerator
    {
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
            catch
            {
                return null;
            }

            using (var reader = new BinaryReader(fs))
            {
                var cacheBINLoader = new MafiaFormats.CacheBINLoader();

                cacheBINLoader.ReadCache(reader);
                fs.Close();

                foreach (var obj in cacheBINLoader.objects)
                {
                    var parentObject = new GameObject(obj.objectName);
                    parentObject.transform.parent = rootObject.transform;

                    int i=0;

                    foreach (var instance in obj.instances)
                    {
                        var model = GameAPI.instance.modelGenerator.LoadObject(Path.Combine("models", instance.modelName), null);

                        if (model == null)
                            continue;

                        model.name = "Instance " + i++;
                        model.transform.parent = parentObject.transform;
                        model.transform.localPosition = instance.pos;
                        model.transform.localRotation = instance.rot;
                        model.transform.localScale = instance.scale;
                        
                    }
                }

                var def = rootObject.AddComponent<CityDefinition>();
                def.data = cacheBINLoader;
            }

            //StoreChachedObject(path, rootObject);

            return rootObject;
        }

        private void ApplyMeshColliderToMeshNode(Mission mission, string meshName)
        {
            var objectToBeColisioned = BaseGenerator.FetchReference(mission, meshName);

            if (objectToBeColisioned)
            {
                var objectFilter = objectToBeColisioned.GetComponent<MeshFilter>();
                if (objectFilter)
                {
                    var newCollider = objectToBeColisioned.AddComponent<MeshCollider>();
                    newCollider.sharedMesh = objectFilter.sharedMesh;
                }
                else
                {
                    for (var i = 0; i < objectToBeColisioned.transform.childCount; i++)
                    {
                        var objectChild = objectToBeColisioned.transform.GetChild(i);
                        var objectChildFilter = objectChild.GetComponent<MeshFilter>();

                        if (objectChildFilter)
                        {
                            var newCollider = objectChild.gameObject.AddComponent<MeshCollider>();
                            newCollider.sharedMesh = objectChildFilter.sharedMesh;
                        }
                    }
                }
            }
        }

        public GameObject LoadCollisions(Mission mission, string path)
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
            catch
            {
                return null;
            }

            using (var reader = new BinaryReader(fs))
            {
                var newKlzLoader = new MafiaFormats.KLZLoader();
                newKlzLoader.load(reader);
                fs.Close();

                //NOTE(DavoSK): All face colls are inside of mesh nodes not in tree.klz
                //tree.klz node game object contains only primitive colliders.

                //Check for face colisioned meshed and add mesh collider
                string lastFaceColledMesh = "";
                foreach (var faceCol in newKlzLoader.faceCols)
                {
                    var link = faceCol.indices[0].link;
                    if (lastFaceColledMesh != newKlzLoader.linkTables[link].name)
                    {
                        lastFaceColledMesh = newKlzLoader.linkTables[link].name;
                        ApplyMeshColliderToMeshNode(mission, lastFaceColledMesh);
                    }
                }

                //Cylinder are mesh colided in unity for us :)
                foreach (var cylCol in newKlzLoader.cylinderCols)
                {
                    var linkName = newKlzLoader.linkTables[(int)cylCol.link].name;
                    ApplyMeshColliderToMeshNode(mission, linkName);
                }

                //Load spehere collisions
                foreach (var sphereCol in newKlzLoader.sphereCols)
                {
                    var linkName = newKlzLoader.linkTables[(int)sphereCol.link].name;
                    var objectToBeColisioned = new GameObject(linkName);
                    if (objectToBeColisioned)
                    {
                        objectToBeColisioned.transform.parent = rootObject.transform;
                        objectToBeColisioned.transform.position = sphereCol.position;

                        var sphereCollider = objectToBeColisioned.AddComponent<SphereCollider>();
                        sphereCollider.radius = sphereCol.radius;
                    }
                }
                
                //Load ABB Colls Needs more math
                foreach (var ABBCol in newKlzLoader.AABBCols)
                {
                    var linkName = newKlzLoader.linkTables[(int)ABBCol.link].name;
                    var objectToBeColisioned = new GameObject(linkName);
                    if (objectToBeColisioned)
                    {
                        objectToBeColisioned.transform.parent = rootObject.transform;
                        
                        Vector3 p1 = ABBCol.min;
                        Vector3 p2 = ABBCol.max;
                        Vector3 center = (p1 + p2) / 2.0f;
                        Vector3 bboxCorner = p2 - center;

                        objectToBeColisioned.transform.position = center;
                    
                        var boxCollider = objectToBeColisioned.AddComponent<BoxCollider>();
                        boxCollider.size = bboxCorner*2;
                    } 
                }
               

                //Load XTOBB Cols
                foreach (var XTOBBCol in newKlzLoader.XTOBBCols)
                {
                    var linkName = newKlzLoader.linkTables[(int)XTOBBCol.link].name;
                    var objectToBeColisioned = new GameObject(linkName);
                    if (objectToBeColisioned)
                    {
                        objectToBeColisioned.transform.parent = rootObject.transform;
                        objectToBeColisioned.transform.localScale = MatrixExtension.ExtractScale(XTOBBCol.transform);
                        objectToBeColisioned.transform.localRotation = MatrixExtension.ExtractRotation(XTOBBCol.transform);
                        objectToBeColisioned.transform.position = MatrixExtension.ExtractPosition(XTOBBCol.transform);

                        var boxCollider = objectToBeColisioned.AddComponent<BoxCollider>();
                        
                        Vector3 p1 = XTOBBCol.extends[0];
                        Vector3 p2 = XTOBBCol.extends[1];
                        Vector3 center = (p1 + p2) / 2.0f;
                        Vector3 bboxCorner = p2 + center;

                        boxCollider.size = bboxCorner*2;
                        boxCollider.center = center;
                    }
                }

                //Load OBB Cols
                foreach (var OBBCol in newKlzLoader.OBBCols)
                {
                    var linkName = newKlzLoader.linkTables[(int)OBBCol.link].name;
                    var objectToBeColisioned = new GameObject(linkName);
                    if (objectToBeColisioned)
                    {
                        objectToBeColisioned.transform.parent = rootObject.transform;
                        objectToBeColisioned.transform.localScale = MatrixExtension.ExtractScale(OBBCol.transform);
                        objectToBeColisioned.transform.localRotation = MatrixExtension.ExtractRotation(OBBCol.transform);
                        objectToBeColisioned.transform.position = MatrixExtension.ExtractPosition(OBBCol.transform);

                        var boxCollider = objectToBeColisioned.AddComponent<BoxCollider>();

                        Vector3 p1 = OBBCol.extends[0];
                        Vector3 p2 = OBBCol.extends[1];
                        Vector3 center = (p1 + p2) / 2.0f;
                        Vector3 bboxCorner = p2 + center;

                        boxCollider.size = bboxCorner*2;
                        boxCollider.center = center;
                    }
                }

                StoreChachedObject(path, rootObject);

                return rootObject;
            }
        }
    }
}
