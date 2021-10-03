using System.Collections.Generic;
using Appalachia.Core.Collections;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR

#endif

namespace Appalachia.Core.Lighting.Probes
{
    public class TerrainAutomaticLightProbeGroup : AutomaticLightProbeGroup
    {
#if UNITY_EDITOR

        [BoxGroup("Probe Limits"), LabelText("Is Budget Per Terrain")]
        public bool budgetIsPerTerrain = true;

        [BoxGroup("Probe Limits"), LabelText("Is Budget Per Terrain")]
        [PropertyRange(8, 128)]
        public int gridDensity = 32;
        
        private readonly HashSet<Terrain> _terrains = new HashSet<Terrain>();
        
        protected override bool ConsiderCollidables => false;

        protected override float GeometryBackoff => 5.0f;

        protected override string LightProbeGroupName => "_TERRAIN_LIGHT_PROBE_GROUP";

        protected override int TargetCount => _terrains.Count;

        protected override void RecreateTargetList()
        {
            _terrains.Clear();

            var terrains = FindObjectsOfType<Terrain>();

            for (var index = 0; index < terrains.Length; index++)
            {
                var terrain = terrains[index];
                if (cullingMask == (cullingMask | (1 << terrain.gameObject.layer)))
                {
                    _terrains.Add(terrain);

                    var c = terrain.GetComponent<TerrainCollider>();

                    colliders.Add(c);
                }
            }
        }

        protected override void GenerateProbesForTargets(AppaList<Vector3> points, ref bool canceled)
        {
            //var count = 0;
            foreach (var t in _terrains) // if terrains are selected, spawn at their vertex positions and above them.
            {
                for (var x = 0; x < gridDensity; x++)
                {
                    for (var z = 0; z < gridDensity; z++)
                    {
                        var count = (float) ((x * gridDensity) + (z * gridDensity));
                        var denominator = (float)(gridDensity * gridDensity);

                        var progress = count / denominator;

                        if (z % logStep == 0)
                        {
                            canceled = EditorUtility.DisplayCancelableProgressBar(
                                $"AutoProbe: Generating Light Probes ({gameObject.name})",
                                $"Adding points for terrain [{t.name}].",
                                progress
                            );
                        }

                        if (canceled)
                        {
                            EditorUtility.ClearProgressBar();
                            return;
                        }

                        var wsPos = t.GetPosition() +
                            new Vector3(
                                (x / (float)gridDensity) * t.terrainData.heightmapScale.x * t.terrainData.heightmapResolution,
                                0,
                                (z / (float)gridDensity) * t.terrainData.heightmapScale.z * t.terrainData.heightmapResolution
                            );
                        wsPos.y += t.SampleHeight(wsPos) + GeometryBackoff;

                        if (IsInsideBounds(wsPos, false))
                        {
                            points.Add(wsPos);
                            AddToSpatialHash(wsPos);
                        }

                        wsPos.y += heightOffset;
                        if (IsInsideBounds(wsPos, false))
                        {
                            points.Add(wsPos);
                            AddToSpatialHash(wsPos);
                        }
                    }
                }
            }
        }
#endif
    }
}
