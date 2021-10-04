using System.Collections.Generic;
using Appalachia.Core.Collections;
using AwesomeTechnologies.VegetationStudio;
using AwesomeTechnologies.VegetationSystem;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

#endif

namespace Appalachia.Lighting.Probes
{
    public class PersistentVegetationAutomaticLightProbeGroup : AutomaticLightProbeGroup
    {
#if UNITY_EDITOR

        protected override float GeometryBackoff => .1f;

        protected override string LightProbeGroupName => "_VSP_STORAGE_LIGHT_PROBE_GROUP";

        protected override int TargetCount => 1;

        protected override bool ConsiderCollidables => false;

        protected override void RecreateTargetList()
        {

        }

        protected override void GenerateProbesForTargets(AppaList<Vector3> points, ref bool canceled)
        {
            var systemList = VegetationStudioManager.Instance.VegetationSystemList;
            if (systemList == null || systemList.Count == 0) return;

            var system = systemList[0];
            
            if (system == null ||
                system.PersistentVegetationStorage == null ||
                system.PersistentVegetationStorage.PersistentVegetationStoragePackage == null)
            {
                return;
            }
            
            var veggieInfos = new Dictionary<string, VegetationItemInfoPro>();

            for (var packageIndex = 0; packageIndex < system.VegetationPackageProList.Count; packageIndex++)
            {
                var package = system.VegetationPackageProList[packageIndex];
                for (var vegIndex = 0; vegIndex < package.VegetationInfoList.Count; vegIndex++)
                {
                    var info = package.VegetationInfoList[vegIndex];
                    veggieInfos.Add(info.VegetationItemID, info);
                }
            }
            
            foreach (var cell in system.PersistentVegetationStorage.PersistentVegetationStoragePackage
                .PersistentVegetationCellList)
            {
                foreach (var info in cell.PersistentVegetationInfoList)
                {
                    var veggie = veggieInfos[info.VegetationItemID];

                    var bounds = veggie.Bounds;
                    var center = bounds.center;
                    var top = bounds.max.y;

                    var point = new Vector3(center.x, top, center.y);

                    var itemCount = 0;
                    var items = info.VegetationItemList.Count;

                    foreach (var item in info.VegetationItemList)
                    {
                        itemCount += 1;

                        if (itemCount % logStep == 0)
                        {
                            canceled = EditorUtility.DisplayCancelableProgressBar(
                                $"AutoProbe: Generating Light Probes ({gameObject.name})",
                                $"Adding point for mesh [{veggie.VegetationPrefab.name}]:  Instances [{items}]",
                                itemCount / (float) items
                            );
                        }

                        if (canceled)
                        {
                            EditorUtility.ClearProgressBar();
                            return;
                        }

                        var testPoint = item.Position + point;

                        if (IsInsideBounds(testPoint, false))
                        {
                            points.Add(testPoint);
                            AddToSpatialHash(testPoint);
                        }
                    }
                }
            }
        }
#endif
    }
}