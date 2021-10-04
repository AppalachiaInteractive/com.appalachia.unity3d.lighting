using System.Collections.Generic;
using Appalachia.Core.Collections;
using Appalachia.Core.Extensions;
using AwesomeTechnologies.VegetationStudio;
using AwesomeTechnologies.VegetationSystem;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

#endif

namespace Appalachia.Lighting.Probes
{
    public class VegetationSystemAutomaticLightProbeGroup : AutomaticLightProbeGroup
    {
#if UNITY_EDITOR

        protected override float GeometryBackoff => .1f;

        protected override string LightProbeGroupName => "_VSP_LIGHT_PROBE_GROUP";

        protected override int TargetCount => 1;

        protected override bool ConsiderCollidables => false;

        protected override void RecreateTargetList()
        {
        }

        protected override void GenerateProbesForTargets(
            AppaList<Vector3> points,
            ref bool canceled)
        {
            var systemList = VegetationStudioManager.Instance.VegetationSystemList;
            if ((systemList == null) || (systemList.Count == 0))
            {
                return;
            }

            var system = systemList[0];
            var veggieInfos = new Dictionary<string, VegetationItemInfoPro>();

            for (var packageIndex = 0;
                packageIndex < system.VegetationPackageProList.Count;
                packageIndex++)
            {
                var package = system.VegetationPackageProList[packageIndex];
                for (var vegIndex = 0; vegIndex < package.VegetationInfoList.Count; vegIndex++)
                {
                    var info = package.VegetationInfoList[vegIndex];
                    veggieInfos.Add(info.VegetationItemID, info);
                }
            }

            for (var cellIndex = 0; cellIndex < system.VegetationCellList.Count; cellIndex++)
            {
                var cell = system.VegetationCellList[cellIndex];

                for (var packageIndex = 0;
                    packageIndex < cell.VegetationPackageInstancesList.Count;
                    packageIndex++)
                {
                    var packageInstance = cell.VegetationPackageInstancesList[packageIndex];
                    var package = system.VegetationPackageProList[packageIndex];

                    for (var itemIndex = 0;
                        itemIndex < packageInstance.VegetationItemMatrixList.Count;
                        itemIndex++)
                    {
                        var matrices = packageInstance.VegetationItemMatrixList[itemIndex];
                        var info = package.VegetationInfoList[itemIndex];

                        if (info.EnableExternalRendering)
                        {
                            continue;
                        }

                        if (info.VegetationType == VegetationType.Grass)
                        {
                            continue;
                        }

                        if (info.VegetationType == VegetationType.Objects)
                        {
                            continue;
                        }

                        if (info.Bounds.size.magnitude < 1.0)
                        {
                            continue;
                        }

                        var veggie = veggieInfos[info.VegetationItemID];

                        var bounds = veggie.Bounds;
                        var center = bounds.center;
                        var top = bounds.max.y;

                        var point = new Vector3(center.x, top, center.y);

                        var itemCount = 0;
                        var items = matrices.Length;

                        foreach (var item in matrices)
                        {
                            itemCount += 1;

                            if ((itemCount % logStep) == 0)
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

                            var testPoint = item.Matrix.GetPositionFromMatrix() + point;

                            if (IsInsideBounds(testPoint, false))
                            {
                                points.Add(testPoint);
                                AddToSpatialHash(testPoint);
                            }
                        }
                    }
                }
            }
        }
#endif
    }
}
