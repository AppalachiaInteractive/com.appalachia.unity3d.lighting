using System.Collections.Generic;
using System.Linq;
using Appalachia.Core.Extensions;
using AwesomeTechnologies.VegetationStudio;
using AwesomeTechnologies.VegetationSystem;
using UnityEditor;
using UnityEngine;

namespace Appalachia.Core.Lighting.Occlusion
{
    public static class VegetationStudioOcclusionProbeIntegration
    {
        //private static List<GameObject> destroyOnComplete;

        private const int logThreshold = 25;

        public static void BakeStarted(GameObject occlusionBakeObjects)
        {
            occlusionBakeObjects.SetActive(false);

            BakeCompleted(occlusionBakeObjects);

            var manager = VegetationStudioManager.Instance;

            //var startTime = Time.realtimeSinceStartup;
            //var elapsed = 0.0f;

            try
            {
                var cancellation = false;

                for (var i = 0; i < occlusionBakeObjects.transform.childCount; i++)
                {
                    Object.DestroyImmediate(occlusionBakeObjects.transform.GetChild(i).gameObject);
                }

                Debug.LogWarning("Ensuring lightmap scales are set to 0.");

                //var systemCount = manager.VegetationSystemList.Count;
                var cellCount = manager.VegetationSystemList.Sum(s => s.VegetationCellList.Count);
                //var packageCount = manager.VegetationSystemList.Sum(vsl => vsl.VegetationPackageProList.Count);

                var infoCount = manager.VegetationSystemList.Sum(vsl => vsl.VegetationPackageProList.Sum(vpp => vpp.VegetationInfoList.Count));

                var infoSum = 0;

                foreach (var system in manager.VegetationSystemList)
                {
                    foreach (var package in system.VegetationPackageProList)
                    {
                        foreach (var info in package.VegetationInfoList)
                        {
                            infoSum += 1;

                            if (!Application.isPlaying)
                            {
                                cancellation = cancellation ||
                                               EditorUtility.DisplayCancelableProgressBar(
                                                   "Setting lightmap scales: " + info.Name,
                                                   $"{infoSum}/{infoCount}",
                                                   infoSum / (float) infoCount
                                               );
                            }

                            if (cancellation)
                            {
                                EditorUtility.ClearProgressBar();
                                return;
                            }

                            if ((info == null) || !info.EnableOcclusionBake)
                            {
                                continue;
                            }

                            var prefab = info.VegetationPrefab;

                            prefab.EnableGIForBakeOnly();
                        }
                    }
                }

                Debug.LogWarning("Building matrix lookup from storage.");

                var instances = new Dictionary<VegetationItemInfoPro, (GameObject prefab, HashSet<Matrix4x4> matrices)>();

                var cellSum = 0;

                for (var index = 0; index < manager.VegetationSystemList.Count; index++)
                {
                    var system = manager.VegetationSystemList[index];
                    if (system.PersistentVegetationStorage == null)
                    {
                        continue;
                    }

                    var storage = system.PersistentVegetationStorage;

                    var storagePackage = storage.PersistentVegetationStoragePackage;

                    if (storagePackage == null)
                    {
                        break;
                    }

                    var cellList = storagePackage.PersistentVegetationCellList;

                    foreach (var cell in cellList)
                    {
                        var itemInfoList = cell.PersistentVegetationInfoList;

                        cellSum += 1;

                        if (!Application.isPlaying)
                        {
                            cancellation = cancellation ||
                                           EditorUtility.DisplayCancelableProgressBar(
                                               "Getting matrices from storage: Cell [" + cellSum + "]",
                                               $"{cellSum}/{cellCount}",
                                               cellSum / (float) cellCount
                                           );
                        }

                        if (cancellation)
                        {
                            EditorUtility.ClearProgressBar();
                            return;
                        }

                        for (var infoIndex = 0; infoIndex < itemInfoList.Count; infoIndex++)
                        {
                            var itemInfo = itemInfoList[infoIndex];
                            var info = storage.VegetationSystemPro.GetVegetationItemInfo(itemInfo.VegetationItemID);

                            if ((info == null) || !info.EnableOcclusionBake)
                            {
                                continue;
                            }

                            for (var itemIndex = 0; itemIndex < itemInfo.VegetationItemList.Count; itemIndex++)
                            {
                                var vegetationItem = itemInfo.VegetationItemList[itemIndex];
                                var itemMatrix = Matrix4x4.TRS(
                                    system.VegetationSystemPosition + vegetationItem.Position,
                                    vegetationItem.Rotation,
                                    vegetationItem.Scale
                                );

                                var prefab = info.VegetationPrefab;

                                if (!instances.ContainsKey(info))
                                {
                                    instances.Add(info, (prefab, new HashSet<Matrix4x4>()));
                                }

                                instances[info].matrices.Add(itemMatrix);
                            }
                        }
                    }
                }

                for (var systemIndex = 0; systemIndex < manager.VegetationSystemList.Count; systemIndex++)
                {
                    var system = manager.VegetationSystemList[systemIndex];
                    for (var packageIndex = 0; packageIndex < system.VegetationPackageProList.Count; packageIndex++)
                    {
                        var package = system.VegetationPackageProList[packageIndex];
                        for (var infoIndex = 0; infoIndex < package.VegetationInfoList.Count; infoIndex++)
                        {
                            var info = package.VegetationInfoList[infoIndex];
                            infoSum += 1;

                            if (!Application.isPlaying)
                            {
                                cancellation = cancellation ||
                                               EditorUtility.DisplayCancelableProgressBar(
                                                   "Getting matrices from spawn settings: " + info.Name,
                                                   $"{infoSum}/{infoCount}",
                                                   infoSum / (float) infoCount
                                               );
                            }

                            if (cancellation)
                            {
                                EditorUtility.ClearProgressBar();
                                return;
                            }

                            if (info.EnableOcclusionBake)
                            {
                                var originalSpawn = info.EnableRuntimeSpawn;

                                info.EnableRuntimeSpawn = true;

                                var prefab = info.VegetationPrefab;

                                if (!instances.ContainsKey(info))
                                {
                                    instances.Add(info, (prefab, new HashSet<Matrix4x4>()));
                                }

                                for (var cellIndex = 0; cellIndex < system.VegetationCellList.Count; cellIndex++)
                                {
                                    var vegetationCell = system.VegetationCellList[cellIndex];
                                    system.SpawnVegetationCell(vegetationCell, info.VegetationItemID);

                                    var vegetationInstanceList = system.GetVegetationItemInstances(vegetationCell, info.VegetationItemID);

                                    for (var j = 0; j <= (vegetationInstanceList.Length - 1); j++)
                                    {
                                        var vegetationItemMatrix = vegetationInstanceList[j].Matrix;

                                        instances[info].matrices.Add(vegetationItemMatrix);
                                    }

                                    vegetationCell.ClearCache();
                                }

                                info.EnableRuntimeSpawn = originalSpawn;
                            }
                        }
                    }
                }

                var instanceCount = instances.Sum(i => i.Value.matrices.Count);
                var instanceSum = 0;

                Debug.LogWarning("Deploying instances to scene.");

                var layerID = LayerMask.NameToLayer(OcclusionProbes.OcclusionBake);

                foreach (var instance in instances)
                {
                    var prefab = instance.Value.prefab;
                    var matrices = instance.Value.matrices;

                    GameObject instantiated = null;

                    foreach (var matrix in matrices)
                    {
                        instanceSum += 1;

                        if (!Application.isPlaying && ((instanceSum % logThreshold) == 0))
                        {
                            cancellation = EditorUtility.DisplayCancelableProgressBar(
                                "Deploying instances to scene: [" + instance.Value.prefab.name + "]",
                                $"{instanceSum}/{instanceCount}",
                                instanceSum / (float) instanceCount
                            );
                        }

                        if (cancellation)
                        {
                            EditorUtility.ClearProgressBar();
                            return;
                        }

                        GameObject go;

                        if (instantiated == null)
                        {
                            instantiated = PrefabUtility.InstantiatePrefab(prefab, occlusionBakeObjects.transform) as GameObject;
                            go = instantiated;
                        }
                        else
                        {
                            go = Object.Instantiate(instantiated, occlusionBakeObjects.transform);
                        }

                        go.transform.Matrix4x4ToTransform(matrix);
                        go.layer = layerID;
                    }
                }

                Debug.LogWarning("Enabling occlusion components.");

                var transforms = occlusionBakeObjects.GetComponentsInChildren<Transform>();

                for (var i = 0; i < transforms.Length; i++)
                {
                    var item = transforms[i];

                    if (!Application.isPlaying && ((i % logThreshold) == 0))
                    {
                        cancellation = EditorUtility.DisplayCancelableProgressBar(
                            "Setting occlusion bake layers: [" + item.gameObject.name + "]",
                            $"{i}/{transforms.Length}",
                            i / (float) transforms.Length
                        );
                    }

                    if (cancellation)
                    {
                        EditorUtility.ClearProgressBar();
                        return;
                    }

                    if (item.CompareTag(OcclusionProbes.OcclusionBake))
                    {
                        var components = item.gameObject.GetComponents<Behaviour>();
                        for (var j = 0; j < components.Length; j++)
                        {
                            components[j].enabled = true;
                        }
                    }
                    else
                    {
                        var components = item.gameObject.GetComponents<Component>();

                        for (var j = 0; j < components.Length; j++)
                        {
                            var component = components[j];

                            if (component is MeshCollider mc)
                            {
                                mc.enabled = true;
                                mc.gameObject.SetActive(true);
                            }
                        }
                    }
                }

                occlusionBakeObjects.SetActive(true);

                occlusionBakeObjects.MoveToLayerRecursive(layerID);

                Debug.LogWarning("Occlusion bake deployment completed.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static void BakeCompleted(GameObject occlusionBakeObjects)
        {
            Debug.LogWarning("Removing deployed instances.");

            try
            {
                occlusionBakeObjects.SetActive(false);

                var children = occlusionBakeObjects.transform.childCount;

                for (var i = children - 1; i >= 0; i--)
                {
                    if ((i % logThreshold) == 0)
                    {
                        if (!Application.isPlaying)
                        {
                            EditorUtility.DisplayProgressBar("Deleting occlusion bake items", $"Spawn cell {i}/{children}", i / (float) children);
                        }
                    }

                    Object.DestroyImmediate(occlusionBakeObjects.transform.GetChild(i).gameObject);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
