using System.Collections.Generic;
using Appalachia.Core.Collections;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
#if UNITY_EDITOR

#endif

namespace Appalachia.Core.Lighting.Probes
{
    public class StaticAutomaticLightProbeGroup : AutomaticLightProbeGroup
    {
#if UNITY_EDITOR

        protected readonly HashSet<MeshFilter> hash_meshFilters = new HashSet<MeshFilter>();

        protected override bool ConsiderCollidables => false;

        protected override float GeometryBackoff => .15f;

        protected override string LightProbeGroupName => "_STATIC_LIGHT_PROBE_GROUP";

        protected override int TargetCount => hash_meshFilters.Count;
        
        [BoxGroup("Vertex Placement"), LabelText("Test Points Per Mesh")]
        [PropertyRange(4, 128)]
        public int maxVerticesPerMesh = 4;

        private bool _considerCollidables;

        protected override void RecreateTargetList()
        {
            // expand spawnObjects lists to include meshes, terrain, and colliders
            hash_meshFilters.Clear();
            colliders.Clear();

            var gos = FindObjectsOfType<Transform>();

            foreach (var go in gos)
            {
                if (cullingMask == (cullingMask | (1 << go.gameObject.layer)))
                {
                    var flags = GameObjectUtility.GetStaticEditorFlags(go.gameObject);

                    if (flags.HasFlag(StaticEditorFlags.ContributeGI))
                    {
                        var mr = go.GetComponent<MeshRenderer>();
                        var mf = go.GetComponent<MeshFilter>();

                        if (mr != null && mf != null)
                        {
                            hash_meshFilters.Add(mf);
                            
                            var cs = go.GetComponents<Collider>();

                            foreach (var c in cs)
                            {
                                if (cullingMask == (cullingMask | (1 << c.gameObject.layer)))
                                {
                                    colliders.Add(c);
                                }
                            }
                        }
                    }
                }
            }
        }

        protected override void GenerateProbesForTargets(AppaList<Vector3> points, ref bool canceled)
        {
            var count = 0;

            // if meshes are selected, spawn at their vertex positions and above them.
            foreach (var mf in hash_meshFilters)
            {
                canceled = EditorUtility.DisplayCancelableProgressBar(
                    $"AutoProbe: Generating Light Probes ({gameObject.name})",
                    $"Adding points for mesh [{mf.name}]:  Total [{mf.sharedMesh.vertexCount}]",
                    count / (float) hash_meshFilters.Count
                );

                count += 1;

                if (canceled)
                {
                    EditorUtility.ClearProgressBar();
                    return;
                }

                var verts = mf.sharedMesh.vertices;
                var normals = mf.sharedMesh.normals;

                int step;

                step = (int) ((float)verts.Length / maxVerticesPerMesh);
                

                if (step < 1)
                {
                    step = 1;
                }

                for (var index = 0; index < verts.Length; index += step)
                {
                    var v = verts[index];
                    var n = normals[index];
                    
                    var pos = mf.transform.TransformPoint(v); // put v into world space
                    var nor = mf.transform.TransformDirection(n);
                    
                    pos += (nor*heightOffset);

                    if (IsInsideBounds(pos, false))
                    {
                        points.Add(pos);
                        AddToSpatialHash(pos);
                    }
                }
            }
        }

#endif
    }
}