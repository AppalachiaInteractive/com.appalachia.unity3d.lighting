using Appalachia.Core.Collections;
using Appalachia.Prefabs.Core;
using Appalachia.Prefabs.Rendering;
using Appalachia.Prefabs.Rendering.Runtime;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR

#endif

namespace Appalachia.Lighting.Probes
{
    public class PrefabRenderingManagerAutomaticLightProbeGroup : AutomaticLightProbeGroup
    {
#if UNITY_EDITOR

        protected override float GeometryBackoff => .1f;

        protected override string LightProbeGroupName => "_PREFAB_REN_MAN_LIGHT_PROBE_GROUP";

        protected override int TargetCount => 1;
        
        protected override bool ConsiderCollidables => false;

        protected override void RecreateTargetList()
        {
           
        }


        private PrefabModelType[] _allowedTypes = {
            PrefabModelType.ObjectHuge,
            PrefabModelType.AssemblyHuge,
            PrefabModelType.AssemblyLarge,
            PrefabModelType.ObjectLarge,
            PrefabModelType.TreeLarge,
            PrefabModelType.TreeMedium,
        };
        
        protected override void GenerateProbesForTargets(AppaList<Vector3> points, ref bool canceled)
        {
            var instance = PrefabRenderingManager.instance;

            if (!instance.enabled ||
                instance.gpui == null ||
                !instance.gpui.gpuiSimulator.simulateAtEditor)
            {
                return;
            }

            var renderingSets = PrefabRenderingManager.instance.renderingSets;
            
            for(var i = 0; i < renderingSets.Sets.Count; i++)
            {
                var data = renderingSets.Sets.at[i];
                
                if (data.instanceManager.currentState != RuntimeStateCode.Enabled) continue;

                var found = false;
                for (var j = 0; j < _allowedTypes.Length; j++)
                {
                    if (_allowedTypes[j] == data.modelType)
                    {
                        found = true;
                        break;
                    }
                }
                
                if (!found) continue;
                              
                var bounds = data.bounds;
                
                if (bounds.size.magnitude < 1.0) continue;
                
                var center = bounds.center;
                var top = bounds.max.y;

                var point = new Vector3(center.x, top, center.y);

                var itemCount = 0;

                if (data.instanceManager.element?.instances == null || data.instanceManager.element.instances.Length <= 0)
                {
                    continue;
                }
                
                var items = data.instanceManager.element.instances.Length;

                foreach (var position in data.instanceManager.element.positions)
                {
                    itemCount += 1;

                    if (itemCount % logStep == 0)
                    {
                        canceled = EditorUtility.DisplayCancelableProgressBar(
                            $"AutoProbe: Generating Light Probes ({gameObject.name})",
                            $"Adding point for mesh [{data.prefab.name}]:  Instances [{items}]",
                            itemCount / (float) items
                        );   
                    }

                    if (canceled)
                    {
                        EditorUtility.ClearProgressBar();
                        return;
                    }

                    var testPoint = (Vector3)position + point;

                    if (IsInsideBounds(testPoint, false))
                    {
                        points.Add(testPoint);
                        AddToSpatialHash(testPoint);
                    }
                }
            }
        }
#endif
    }
}