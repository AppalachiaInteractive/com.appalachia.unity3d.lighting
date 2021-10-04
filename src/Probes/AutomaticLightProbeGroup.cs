using System;
using System.Collections.Generic;
using System.IO;
using Appalachia.Core.Collections;
using Appalachia.Core.Collections.Implementations.Lists;
using Appalachia.Editing.Attributes;
using Appalachia.Spatial.Terrains.Utilities;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using Random = UnityEngine.Random;

#endif

namespace Appalachia.Lighting.Probes
{
    [ExecuteAlways]
    public abstract class AutomaticLightProbeGroup : MonoBehaviour
    {
#if UNITY_EDITOR

        protected static int logStep = 100;
        
        private const int _LIST_SIZE = 128;
        
        [HideInInspector] public LightProbeGroup lpg;

        [BoxGroup("Instance Lock")]
        [SmartLabel]
        public bool locked;

        [BoxGroup("Collisions")]
        [ReadOnly]
        [SmartLabel]
        public GameObject constraints;
        
        [FoldoutGroup("Collisions/Bounds")]
        [ReadOnly]
        [ListDrawerSettings]
        public AppaList_Collider boundsColliders = new AppaList_Collider(_LIST_SIZE);

        [BoxGroup("Collisions")]
        [Tooltip(
            "Use MeshFilter and Terrain vertices to spawn light probes by dragging them (or a parent node) here. You can also set the maximum height relative to these meshes, so probes can be restricted to near-ground areas."
        )]
        [SceneObjectsOnly, ShowIf(nameof(ConsiderCollidables))]
        [ListDrawerSettings]
        public AppaList_GameObject collidables = new AppaList_GameObject(_LIST_SIZE);

        protected abstract bool ConsiderCollidables { get; }

        [BoxGroup("Terrain")]
        [ToggleLeft]
        [SmartLabel]
        public bool allowBelowTerrain;
        
        [BoxGroup("Terrain")] 
        [ToggleLeft]
        [SmartLabel]public bool allowAboveTerrain = true;

        [BoxGroup("Initial Placement")]
        [LabelText("Probe Algorithm")]
        [SmartLabel]
        [Tooltip(
            "Grid style spaces probes uniformly.  Spray style is more organic and explores spaces more organically."
        )]
        public AutomaticLightProbeGeneratorType generatorAlgorithm = AutomaticLightProbeGeneratorType.Spray;

        private bool _showRayCount => generatorAlgorithm == AutomaticLightProbeGeneratorType.Spray;

        [BoxGroup("Initial Placement")]
        [LabelText("Rays Per Point")]
        [SmartLabel]
        [Tooltip("Number of random rays cast per probe position, looking for valid spots to put new probes.")]
        [PropertyRange(1.0f, 24.0f)]
        [ShowIf(nameof(_showRayCount))]
        public int rayCount = 10;

        private string _maxDistanceLabel =>
            generatorAlgorithm == AutomaticLightProbeGeneratorType.Spray ? "Ray Length" : "Grid Size";

        [BoxGroup("Initial Placement")]
        [LabelText("$" + nameof(_maxDistanceLabel))]
        [SmartLabel]
        [Tooltip("Typical spacing between points.")]
        [PropertyRange(1.0f, 32.0f)]
        public float maxDistance = 4.0f;

        [BoxGroup("Initial Placement")]
        [LabelText("Min Probe Distance")]
        [SmartLabel]
        [Tooltip(
            "No probes will be generated that are closer than this distance, unless they are occluded from each other."
        )]
        [PropertyRange(1.0f, 24.0f)]
        public float distBetweenProbes = 3.0f;

        [BoxGroup("Initial Placement")]
        [LabelText("Height offset")]
        [SmartLabel]
        [Tooltip("Probes will be placed no more than this amount above the surface.")]
        [PropertyRange(-10.0f, 10.0f)]
        public float heightOffset = 3.0f;

        [BoxGroup("Initial Placement")]
        [LabelText("Layer Mask")]
        [SmartLabel]
        [Tooltip(
            "Choose the layers that you want rays to consider for collision.  Useful if dynamic objects are mixed into your scene (in a separate layer) but aren't supposed to block light."
        )]
        public LayerMask cullingMask = ~0;

        [BoxGroup("Optimization")]
        [LabelText("Probe Budget")]
        [SmartLabel]
        [Tooltip(
            "Set how many light probes you want in this space.  AutoProbe will generate more, but the Optimize button will remove lower quality probes until the budget is achieved."
        )]
        [PropertyRange(16.0f, 4096.0f)]
        public int probeBudget = 1024;

        [BoxGroup("Stats")]
        [HorizontalGroup("Stats/A")]
        [ShowInInspector]
        [SmartLabel]
        public int probeCount => lpg == null ? 0 : lpg.probePositions.Length;

        [BoxGroup("Stats")]
        [HorizontalGroup("Stats/A")]
        [ShowInInspector]
        [SmartLabel]
        [SuffixLabel("MB")]
        public float dataLength =>
            Lightmapping.lightingDataAsset == null
                ? 0.0f
                : new FileInfo(AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset)).Length / 1024.0f / 1024.0f;

        protected readonly HashSet<Collider> colliders = new HashSet<Collider>();
        
// when a ray hits collision geometry, we step back this much so the light probe knows what side it's on
        protected abstract float GeometryBackoff { get; }

        protected abstract string LightProbeGroupName { get; }

        protected abstract int TargetCount { get; }

        private static readonly RaycastHit[] oneHit = new RaycastHit[1];
        private static readonly RaycastHit[] manyHits = new RaycastHit[100];

        //private Collider[] disabledChildColliders;

        // This is all 6 major axis directions, plus 8 corner directions.  That should be fairly representative.
        private static readonly Vector3[] directions =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.up,
            Vector3.down,
            Vector3.right,
            Vector3.left,
            new Vector3(1, 1, 1).normalized,
            new Vector3(-1, 1, 1).normalized,
            new Vector3(1, -1, 1).normalized,
            new Vector3(1, 1, -1).normalized,
            new Vector3(-1, -1, 1).normalized,
            new Vector3(-1, 1, -1).normalized,
            new Vector3(1, -1, -1).normalized,
            new Vector3(-1, -1, -1).normalized
        };

        private static readonly Color[] aColors = new Color[directions.Length];
        private static readonly Color[] bColors = new Color[directions.Length];

        //-------------------
        // This finds all the known points within radius of pos and fills them out into the points list.
        private readonly int[] sequence = {0, -1, 1}; // it's ideal to always check the center first, then look nearby

        // To generate a hash for a coordinate where we WANT points near each other to collide, we simply divide each coordinate by the size of the coordinate "bucket",
        // and use those values to generate a hash value directly, storing the hash as the key and adding the point to the bucket.  When we request points
        // that are nearby, we simply look at all points in the vicinity of those buckets and do the distance check there, since it should be relatively few points anyway.
        private readonly Dictionary<int, List<Vector3>> spatialHash = new Dictionary<int, List<Vector3>>();

        private float
            resolution =
                1.0f; // the larger this is, the more points end up in the same bucket.  But resolution MUST be at least half the largest query size.

        private float ooResolution = 1.0f;

        public void Awake()
        {
            if (lpg == null)
            {
                lpg = GetComponent<LightProbeGroup>();
            }

            if (lpg == null)
            {
                lpg = gameObject.AddComponent<LightProbeGroup>();
            }

            gameObject.name = LightProbeGroupName;

            var t = transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            ValidateConstraints();
        }

        private void ValidateConstraints()
        {

            if (boundsColliders == null)
            {
                boundsColliders = new AppaList_Collider(_LIST_SIZE);                
            }

            boundsColliders.Clear();

            if (constraints == null)
            {
                var t = transform.Find("Constraints");

                if (t != null)
                {
                    constraints = t.gameObject;
                }
            }

            if (constraints == null)
            {
                constraints = new GameObject("Constraints");
            }

            var boxT = constraints.transform.Find("Box");
            var sphereT = constraints.transform.Find("Sphere");

            if (boxT == null)
            {
                var box = new GameObject("Box");
                boxT = box.transform;
            }

            if (sphereT == null)
            {
                var sphere = new GameObject("Sphere");
                sphereT = sphere.transform;
            }

            constraints.transform.SetParent(gameObject.transform, false);
            boxT.SetParent(constraints.transform, false);
            sphereT.SetParent(constraints.transform, false);

            var boxC = boxT.GetComponent<BoxCollider>();
            var sphereC = sphereT.GetComponent<SphereCollider>();

            if (boxC == null)
            {
                boxC = boxT.gameObject.AddComponent<BoxCollider>();
            }

            if (sphereC == null)
            {
                sphereC = sphereT.gameObject.AddComponent<SphereCollider>();
            }

            boxC.enabled = false;
            sphereC.enabled = false;

            boundsColliders.Add(boxC);
            boundsColliders.Add(sphereC);
        }

        // returns true if there's a problem
        public bool HasDisabledChildColliders(bool showError)
        {
            // this gets all the colliders, even the enabled ones
            //disabledChildColliders = GetComponentsInChildren<Collider>(true);

            // this is probably because of old autoprobe objects or misunderstanding
            var anyValidColliders = false;

            // this is due to misunderstanding
            var anyRealColliders = false;

            for(var i = 0; i < boundsColliders.Count; i++)
            {
                var c = boundsColliders[i];
                
                if ((c.enabled == false) || (c.gameObject.activeInHierarchy == false))
                {
                    if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider)
                    {
                        anyValidColliders = true;
                    }
                }
                else
                {
                    anyRealColliders = true;
                }
            }

            if (!anyValidColliders || anyRealColliders)
            {
                if (showError)
                {
                    if (anyRealColliders)
                    {
                        EditorUtility.DisplayDialog(
                            "Enabled Constraint Colliders Error (" + gameObject.name + ")",
                            "Do not enable the colliders under AutoProbe.  Leave them disabled, so they do not affect your scene's collision.",
                            "I Promise To Fix It"
                        );
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "No Constraint Colliders Error (" + gameObject.name + ")",
                            "You must keep at least one disabled child collider under AutoProbe to limit light probe generation.",
                            "I Promise To Fix It"
                        );
                    }
                }

                return true;
            }

            return false;
        }

        // NOTE: This is not numerically stable.  It is possible to have a point that is supposed to test inside but has a slightly negative (or perhaps slightly greater than 1.0) value.
        // The way to tell is if 3/4 coordinates are within the range, but one is just barely outside.
        protected static bool IsInsideTetrahedron(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector3 test,
            ref Vector4 coordinates)
        {
            var vap = test - a;
            var vbp = test - b;

            var vab = b - a;
            var vac = c - a;
            var vad = d - a;

            var vbc = c - b;
            var vbd = d - b;

            var v6 = 1.0f / Vector3.Dot(vab, Vector3.Cross(vac, vad));
            coordinates[0] = Vector3.Dot(vbp, Vector3.Cross(vbd, vbc)) * v6;
            coordinates[1] = Vector3.Dot(vap, Vector3.Cross(vac, vad)) * v6;
            coordinates[2] = Vector3.Dot(vap, Vector3.Cross(vad, vab)) * v6;
            coordinates[3] = Vector3.Dot(vap, Vector3.Cross(vab, vac)) * v6;
            return !((coordinates[0] < 0.0f) ||
                (coordinates[1] < 0.0f) ||
                (coordinates[2] < 0.0f) ||
                (coordinates[3] < 0.0f)); // any negatives means coordinate is OUTSIDE, so not of that means inside.
        }

        public static float CompareSH(SphericalHarmonicsL2 a, SphericalHarmonicsL2 b)
        {
            var error = 0.0f; // return the summed error
            a.Evaluate(directions, aColors); // RGB may be negative, in which case we want to treat it as zero.
            b.Evaluate(directions, bColors);
            for (var i = 0; i < directions.Length; i++)
            {
                error += RGBError(aColors[i], bColors[i]);
            }

            return error;
        }

        // An attempt at optimizing away more light probes comes at a cost of more calculations, but I think it's worth it.
        protected static float RGBError(Color a, Color b)
        {
            // Convert XYZ to Lab color representation
            var aL = Mathf.Max(0.0f, (116.0f * a.g) - 16.0f);
            var aa = 500.0f * (a.r - a.g);
            var ab = 200.0f * (a.g - a.b);

            var bL = Mathf.Max(0.0f, (116.0f * b.g) - 16.0f);
            var ba = 500.0f * (b.r - b.g);
            var bb = 200.0f * (b.g - b.b);

            // CIE76 method of DeltaE
            var err = ((aL - bL) * (aL - bL)) +
                ((aa - ba) * (aa - ba)) +
                ((ab - bb) * (ab - bb)); // Lab difference is simply euclidean distance
            return Mathf.Sqrt(err);
        }

        // Check that the point we want to add to the set is inside a "reasonable bounds".  I broke this out into a separate function
        // in case you wanted to get fancy, or simplify the system, or whatever, without having to dig too deeply.
        // Currently, this just requires all points to fit within the DISABLED colliders which are children of this object.  That seems pretty easy and costs you nothing.
        protected bool IsInsideBounds(Vector3 position, bool checkForCollision)
        {
            // If the point is outside the mesh heights limitations, reject it first.
            if ((TargetCount > 0) && checkForCollision)
            {
                var downHits = Physics.RaycastNonAlloc(
                    position,
                    Vector3.down,
                    manyHits,
                    heightOffset,
                    cullingMask,
                    QueryTriggerInteraction.UseGlobal
                );
                if (downHits == 0)
                {
                    return
                        false; // didn't hit anything, which means we are too high or outside the vertical space of the selected meshes
                }

                var rejectProbe = true;

                for (var index = 0; index < downHits; index++)
                {
                    var h = manyHits[index];
                    if (colliders.Contains(h.collider))
                    {
                        rejectProbe = false;
                    }
                }

                if (rejectProbe) // hit other things, but not a collider we care about.  Reject it.
                {
                    return false;
                }
            }

            if (boundsColliders != null)
            {
                for (var i = 0; i < boundsColliders.Count; i++)
                {
                    var c = boundsColliders[i];

                    // do we pay attention to it?
                    if (!c.enabled || !c.gameObject.activeInHierarchy)
                    {
                        var localPosition = c.transform.InverseTransformPoint(position);

                        // Inside a sphere
                        var sc = c as SphereCollider;
                        if (sc != null)
                        {
                            if (Vector3.SqrMagnitude(localPosition - sc.center) <= (sc.radius * sc.radius))
                            {
                                return true;
                            }
                        }

                        // Inside a box
                        var bc = c as BoxCollider;
                        if (bc != null)
                        {
                            var delta = (localPosition - bc.center) +
                                (bc.size * 0.5f); // offset the box by half the size, so we can do a quicker check below
                            if (Vector3.Max(Vector3.zero, delta) == Vector3.Min(delta, bc.size)) // being (too?) clever
                            {
                                return true;
                            }
                        }

                        // Inside a capsule
                        var cc = c as CapsuleCollider;
                        if (cc != null)
                        {
                            var cappedHeight = Mathf.Max(0.0f, cc.height - (cc.radius * 2.0f));
                            float distSqToPoint;
                            if (cappedHeight > 0.0f) // normal case where it is actually shaped like a capsule
                            {
                                var axis = (cc.direction == 0
                                        ? Vector3.right
                                        : cc.direction == 1
                                            ? Vector3.up
                                            : Vector3.forward) *
                                    cappedHeight;
                                var p1 = cc.center - (axis * 0.5f);

                                // perform line test in capsule-space, where bottom capsule sphere is at 0,0,0
                                var delta = localPosition - p1;
                                var d = Mathf.Clamp01(Vector3.Dot(delta, axis) / (cappedHeight * cappedHeight));
                                var closestPointOnLine = p1 + (d * axis);
                                distSqToPoint = Vector3.SqrMagnitude(closestPointOnLine - localPosition);
                            }
                            else // degenerate case with height smaller than the radius of the capsule, making it a sphere
                            {
                                distSqToPoint = Vector3.SqrMagnitude(localPosition - cc.center);
                            }

                            if (distSqToPoint < (cc.radius * cc.radius))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        protected bool HasNearbyPoint(Vector3 pos, float distance)
        {
            var distanceSqr = distance * distance;
            var x = Mathf.RoundToInt(pos.x * ooResolution);
            var y = Mathf.RoundToInt(pos.y * ooResolution);
            var z = Mathf.RoundToInt(pos.z * ooResolution);
            for (var i = 0; i < 3; i++) // check the cube around the bucket we find
            {
                var xm = x + sequence[i];
                for (var j = 0; j < 3; j++)
                {
                    var ym = y + sequence[j];
                    for (var k = 0; k < 3; k++)
                    {
                        var zm = z + sequence[k];
                        var hash = (xm << 24) ^
                            (xm >> 8) ^
                            (ym << 16) ^
                            (ym >> 16) ^
                            (zm << 8) ^
                            (zm >> 24); // probably not the world's best hash, but quick.
                        List<Vector3> o;
                        if (spatialHash.TryGetValue(hash, out o))
                        {
                            foreach (var v in o)
                            {
                                var direction = v - pos;
                                var distSq = direction.sqrMagnitude;
                                if (distSq < Mathf.Epsilon
                                ) // exact point is already in set, this ray is too close to an existing one (happens with grids most of the time)
                                {
                                    return true;
                                }

                                if (distSq < distanceSqr)
                                {
                                    var distBetweenPoints = Mathf.Sqrt(distSq);

                                    // they COULD be too close.  Let's raycast and see if they are considered "visible" from one another.  If they can see each other, they're too close.
                                    if (Physics.RaycastNonAlloc(
                                            pos,
                                            direction / distBetweenPoints,
                                            oneHit,
                                            distBetweenPoints,
                                            cullingMask,
                                            QueryTriggerInteraction.UseGlobal
                                        ) ==
                                        0)
                                    {
                                        return true; // nothing is between these two points, so they're too close
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return false;
        }

        protected void AddToSpatialHash(Vector3 pos)
        {
            var x = Mathf.RoundToInt(pos.x * ooResolution);
            var y = Mathf.RoundToInt(pos.y * ooResolution);
            var z = Mathf.RoundToInt(pos.z * ooResolution);
            var hash = (x << 24) ^ (x >> 8) ^ (y << 16) ^ (y >> 16) ^ (z << 8) ^ (z >> 24);
            List<Vector3> o;
            if (!spatialHash.TryGetValue(hash, out o))
            {
                o = new List<Vector3>(_LIST_SIZE);
                spatialHash.Add(hash, o);
            }

            o.Add(pos);
        }

        protected void InitializeSpatialHash(float bucketResolution)
        {
            spatialHash.Clear();
            resolution = bucketResolution;
            ooResolution = 1.0f / resolution;
        }

        // Cast a ray in some direction and if it hits no geometry use that spot, else if it hits something, move away from the object by its normal slightly.
        // Check for nearby visible probes, and if there aren't any, make one.  Otherwise, fail.
        // Return true if we made a probe.
        protected bool DoCast(
            Queue<Vector3> activeList,
            AppaList<Vector3> allPoints,
            Vector3 pos,
            Vector3 dir,
            float minProbeDist,
            float rayLength)
        {
            var newPos = pos + (dir * rayLength);

            var numhits = Physics.RaycastNonAlloc(
                pos,
                dir,
                oneHit,
                rayLength,
                cullingMask,
                QueryTriggerInteraction.UseGlobal
            );
            if (numhits > 0)
            {
                newPos = oneHit[0].point + (oneHit[0].normal * GeometryBackoff);
            }

            // reject any point outside the bounding radius for this node (which also checks for the height above mesh, if AboveMeshes is enabled)
            if (IsInsideBounds(newPos, true) == false)
            {
                return false;
            }

            // Let's see if there's any point closer than distBetweenPoints, WHICH WE CAN RAYCAST TO.  If so, reject it.  If not, add it.
            if (HasNearbyPoint(newPos, minProbeDist))
            {
                return false;
            }

            if (numhits == 0)
            {
                activeList.Enqueue(newPos); // only consider new points that DON'T hit geometry as active
            }

            allPoints.Add(newPos);
            AddToSpatialHash(newPos);
            return true;
        }

        protected abstract void RecreateTargetList();

        protected abstract void GenerateProbesForTargets(AppaList<Vector3> points, ref bool canceled);

        // Returns the count of new probes generated
        public int GenerateProbesInternal()
        {
            try
            {                
                ValidateConstraints();
                
                // Figure out where we can create probes.
                if (HasDisabledChildColliders(true))
                {
                    return 0; // aborted
                }

                var probePositions = new Vector3[0];
                var lpgDering = true;
                
                if (lpg == null)
                {
                    lpg = GetComponent<LightProbeGroup>();
                }
                
                if (lpg != null)
                {
                    probePositions = lpg.probePositions;

                    lpgDering = lpg.dering;
                    Undo.DestroyObjectImmediate(lpg);
                }

                RecreateTargetList();

                InitializeSpatialHash(maxDistance); // resolution MUST be at least half the largest query size

                var canceled = false;

                // Grab all the light probe positions and move them from local space to world space
                var positions = new AppaList_Vector3(probePositions.Length);
                for (var i = 0; i < probePositions.Length; i++)
                {
                    var wsPos = transform.TransformPoint(probePositions[i]); // move to world space
                    positions.Add(wsPos);
                    AddToSpatialHash(wsPos);
                    if ((i % logStep) == 0)
                    {
                        canceled = EditorUtility.DisplayCancelableProgressBar(
                            $"Generating Light Probes ({gameObject.name})",
                            "Transforming Points",
                            i / (float) positions.Count
                        );

                        if (canceled)
                        {
                            EditorUtility.ClearProgressBar();
                            return 0;
                        }
                    }
                }

                if (positions.Count == 0) // seed this in case all probe positions are empty
                {
                    if (IsInsideBounds(transform.position, true))
                    {
                        positions.Add(transform.position);

                        AddToSpatialHash(
                            transform.position
                        ); // use disabled collider locations and the autoprobe object itself
                    }

                    // initialize with the center points of all the disabled colliders too
                    for(var i = 0; i < boundsColliders.Count; i++)
                    {
                        var c = boundsColliders[i];
                        
                        positions.Add(c.bounds.center);
                        AddToSpatialHash(c.bounds.center);
                    }

                    GenerateProbesForTargets(positions, ref canceled);

                    if (canceled)
                    {
                        return 0;
                    }

                    if (!allowAboveTerrain || !allowBelowTerrain)
                    {
                        FilterPointsFromTerrain(positions);
                    }

                }

                // Now, create an "active" set which we can work with, since a lot of probes will not be on the advancing surface.
                var active = new Queue<Vector3>(positions.Count);

                for (var i = 0; i < positions.Count; i++)
                {
                    active.Enqueue(positions[i]);
                }
                
                var progressTotal = positions.Count;
                var progress = 0;

                while (!canceled && (active.Count > 0))
                {
                    if (progress % logStep == 0)
                    {
                        canceled = EditorUtility.DisplayCancelableProgressBar(
                            $"Generating Light Probes ({gameObject.name})",
                            $"Raymarching... Active [{active.Count}]  Total [{positions.Count}]",
                            progress / (float) progressTotal
                        );
                    }

                    if (canceled)
                    {
                        EditorUtility.ClearProgressBar();
                        return 0;
                    }

                    var currentPoint = active.Dequeue();
                    progress++;

                    switch (generatorAlgorithm)
                    {
                        // cast rays in 6 directions, with very slight randomness to prevent horrible looking tesselations due to floating point errors
                        case AutomaticLightProbeGeneratorType.Grid:
                        {
                            if (DoCast(
                                active,
                                positions,
                                currentPoint,
                                Quaternion.Euler(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) *
                                Vector3.forward,
                                distBetweenProbes,
                                maxDistance
                            ))
                            {
                                progressTotal++;
                            }

                            if (DoCast(
                                active,
                                positions,
                                currentPoint,
                                Quaternion.Euler(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) *
                                Vector3.back,
                                distBetweenProbes,
                                maxDistance
                            ))
                            {
                                progressTotal++;
                            }

                            if (DoCast(
                                active,
                                positions,
                                currentPoint,
                                Quaternion.Euler(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) *
                                Vector3.right,
                                distBetweenProbes,
                                maxDistance
                            ))
                            {
                                progressTotal++;
                            }

                            if (DoCast(
                                active,
                                positions,
                                currentPoint,
                                Quaternion.Euler(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) *
                                Vector3.left,
                                distBetweenProbes,
                                maxDistance
                            ))
                            {
                                progressTotal++;
                            }

                            if (DoCast(
                                active,
                                positions,
                                currentPoint,
                                Quaternion.Euler(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) *
                                Vector3.up,
                                distBetweenProbes,
                                maxDistance
                            ))
                            {
                                progressTotal++;
                            }

                            if (DoCast(
                                active,
                                positions,
                                currentPoint,
                                Quaternion.Euler(Random.value - 0.5f, Random.value - 0.5f, Random.value - 0.5f) *
                                Vector3.down,
                                distBetweenProbes,
                                maxDistance
                            ))
                            {
                                progressTotal++;
                            }

                            break;
                        }

                        // randomly cast N rays
                        case AutomaticLightProbeGeneratorType.Spray:
                        {
                            var keepActive = false;
                            for (var i = 0; i < rayCount; i++)
                            {
                                if (DoCast(
                                    active,
                                    positions,
                                    currentPoint,
                                    Random.onUnitSphere,
                                    distBetweenProbes,
                                    maxDistance
                                ))
                                {
                                    keepActive = true;
                                    progressTotal++;
                                }
                            }

                            if (keepActive
                            ) // in the case where a point generated a new adjacent point, there may yet be unexplored space nearby still, due to sampling error.  Keep trying.
                            {
                                active.Enqueue(currentPoint);
                                progressTotal++;
                            }

                            break;
                        }
                    }
                }

                // Move all world space points back to local space and assign to light probe group
                for (var i = 0; i < positions.Count; i++)
                {
                    positions[i] = transform.InverseTransformPoint(positions[i]);
                    if ((i % logStep) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Generating Light Probes (" + gameObject.name + ")",
                            "Transforming Points",
                            i / (float) positions.Count
                        );
                    }
                }

                lpg = Undo.AddComponent<LightProbeGroup>(gameObject);
                Undo.RecordObject(lpg, "Generate Light Probes");
                lpg.probePositions = positions.ToArray();

                lpg.dering = lpgDering;

                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                Undo.SetCurrentGroupName("Generate Light Probes");
                var newProbes = positions.Count - probePositions.Length;
                return newProbes;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return 0;
        }

        // returns the number of probes removed
        public int OptimizeProbesInternal()
        {
            try
            {
                var errorTolerance = -1.0f; // sentinel value
                var totalRemoved = 0;
                var probePositions = new Vector3[0];

                var lpgDering = true;

                {
                    lpg = GetComponent<LightProbeGroup>();
                    if (lpg != null)
                    {
                        probePositions = lpg.probePositions; // Grab all the light probe positions

                        lpgDering = lpg.dering;
                        Undo.DestroyObjectImmediate(lpg);
                    }
                }

                // move them from local space to world space
                EditorUtility.DisplayProgressBar(
                    "AutoProbe: Optimizing Light Probes (" + gameObject.name + ")",
                    "Moving to world space",
                    0.0f
                );
                var initialProbes = probePositions.Length;
                var p = new AppaList_Vector3(initialProbes);
                //p.Capacity = initialProbes;
                for (var i = 0; i < initialProbes; i++)
                {
                    var wsPos = transform.TransformPoint(probePositions[i]);
                    p.Add(wsPos);
                    probePositions[i] = wsPos;
                    if ((i % logStep) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "AutoProbe: Optimizing Light Probes (" + gameObject.name + ")",
                            "Moving to world space",
                            i / (float) initialProbes
                        );
                    }
                }

                EditorUtility.DisplayProgressBar(
                    "AutoProbe: Optimizing Light Probes (" + gameObject.name + ")",
                    "Moving to world space",
                    1.0f
                );
                
                while (p.Count >= 5) // too few points means skip optimization.  Nothing to remove.
                {
                    
                    // Tetrahedralize the whole set of lightprobes, removing the junk points first.
                    int[] tetraIndices;
                    Vector3[] positions;
                    
                    EditorUtility.DisplayCancelableProgressBar(
                        "AutoProbe: Optimizing Light Probes (" + gameObject.name + ")",
                        "Generating tetrahedrons... Probes [" + initialProbes + "]  Removed [" + totalRemoved + "]",
                        totalRemoved / (float) initialProbes
                    );
                    
                    Lightmapping.Tetrahedralize(probePositions, out tetraIndices, out positions);
                    if (positions.Length != p.Count) // copy back the proper positions to be used
                    {
                        p.RemoveRange(positions.Length, p.Count - positions.Length);
                        for (var i = 0; i < positions.Length; i++)
                        {
                            p[i] = positions[i];
                        }
                    }

                    if (p.Count < 5) // skip optimization.  Nothing to remove.
                    {
                        break;
                    }

                    // Create adjacency neighborhoods for each point, so I can check each one and see if they are necessary.  This is a pretty memory intensive data structure, but temporary.
                    // We do this by walking the tetrahedrons and adding all the vertices in the tetrahedron to each of the vertices IN that tetrahedron.  Then we sort/unique each list, so it has no redundancies.
                    var adjacencyVerts = new AppaList_HashSetOfint(positions.Length);
                    var originalSH = new SphericalHarmonicsL2[positions.Length];
                    var interpProbe = new SphericalHarmonicsL2();
                    var corners = new SphericalHarmonicsL2[4];
                    var tempProbe = new SphericalHarmonicsL2();
                    for (var i = 0; i < positions.Length; i++)
                    {
                        LightProbes.GetInterpolatedProbe(positions[i], null, out tempProbe);
                        originalSH[i] = tempProbe; // cache all the original SH before we jack with them.
                        adjacencyVerts.Add(new HashSet<int>()); // make space for all the verts-to-tetras lists.
                    }

                    for (var i = 0; i < tetraIndices.Length; i += 4) // step by the tetrahedron
                    {
                        for (var j = 0; j < 4; j++) // for each vertex in the tetrahedron
                        {
                            for (var k = 0;
                                k < 4;
                                k++) // add all the vertices in this tetrahedron TO EACH VERTEX'S LIST.
                            {
                                adjacencyVerts[tetraIndices[i + j]].Add(tetraIndices[i + k]);
                            }
                        }
                    }

                    // Walk each vertex and regenerate tetrahedrons for its list of adjacencies.  Then regenerate again with that vertex absent from the list.  If the interpolation is within tolerance,
                    // it is a redundant point and can be removed.  Since it's very, very complicated to remove multiple points from an area and figure out adjacencies again, it's better to just do that
                    // in passes and keep doing passes until there is nothing more to remove.  Should be fairly quick anyway.
                    var perProbeError = new AppaList_float(8);
                    var progress = 0;
                    var totalProgress = positions.Length;
                    var locked =
                        new HashSet<int>(); // these are probes we will not attempt to optimize out on this pass, because one of his neighbors was optimized out already
                    var toRemove = new HashSet<int>(); // these are probes we will optimize out
                    for (var i = 0; i < positions.Length; i++)
                    {
                        if (((i % logStep) == 0) &&
                            EditorUtility.DisplayCancelableProgressBar(
                                "AutoProbe: Optimizing Light Probes (" + gameObject.name + ")",
                                "Interpolating baked light probes... Probes [" +
                                initialProbes +
                                "]  Removed [" +
                                totalRemoved +
                                "]",
                                totalRemoved / (float) initialProbes
                            ))
                        {
                            break;
                        }

                        progress++;

                        var numAdjVerts = adjacencyVerts[i].Count;
                        if (numAdjVerts > 4
                        ) // can never remove a valence vertex from a tetrahedron.  There's no adjacency who can fill in for it
                        {
                            // Skip optimizing this tetrahedron if any of my adjacencies are locked.
                            if (locked.Contains(i) == false
                            ) // only attempt optimizing this vertex away if this specific vertex is not yet locked by an adjacent optimization
                            {
                                // Since we already cached all the light probe SH's, we just need to try making new tetras that don't include p[i] so we can interpolate it from corners.
                                var testPoints = new Vector3[numAdjVerts - 1];
                                var originalIndices = new int[numAdjVerts - 1];
                                var testIndex = 0;
                                foreach (var vi in adjacencyVerts[i])
                                {
                                    if (vi != i)
                                    {
                                        testPoints[testIndex] =
                                            p[vi]; // make a list of positions that does not include p[i]
                                        originalIndices[testIndex] =
                                            vi; // remember what the vertices were in this buffer, for reference
                                        testIndex++;
                                    }
                                }

                                // compute new tetrahedrons from a small set of points, not including the one we're testing						
                                int[] tetraIndices2;
                                Vector3[] positions2;
                                Lightmapping.Tetrahedralize(testPoints, out tetraIndices2, out positions2);

                                // Now, find the tetrahedron that contains the missing vertex position, using 3D barycentric coordinates.
                                var pos = p[i];
                                var bestTetraIndex = 0;
                                var bestMax = Mathf.Infinity;
                                var bestMin = Mathf.NegativeInfinity;
                                var coordinates = Vector4.zero;
                                for (var j = 0; j < tetraIndices2.Length; j += 4)
                                {
                                    var a = positions2[tetraIndices2[j + 0]];
                                    var b = positions2[tetraIndices2[j + 1]];
                                    var c = positions2[tetraIndices2[j + 2]];
                                    var d = positions2[tetraIndices2[j + 3]];
                                    if (IsInsideTetrahedron(a, b, c, d, pos, ref coordinates)
                                    ) // we found the tetra that holds our test point
                                    {
                                        bestTetraIndex = j;
                                        break;
                                    }

                                    // if this is better than the best coordinate we have seen, remember it
                                    var maxV = Mathf.Max(
                                        Mathf.Max(coordinates[0], coordinates[1]),
                                        Mathf.Max(coordinates[2], coordinates[3])
                                    );
                                    var minV = Mathf.Min(
                                        Mathf.Min(coordinates[0], coordinates[1]),
                                        Mathf.Min(coordinates[2], coordinates[3])
                                    );
                                    if ((maxV <= bestMax) && (minV >= bestMin))
                                    {
                                        bestMax = maxV;
                                        bestMin = minV;
                                        bestTetraIndex = j;
                                    }
                                }

                                // without having to re-compute, just pull these from the array we fetched initially
                                corners[0] = originalSH[originalIndices[tetraIndices2[bestTetraIndex + 0]]];
                                corners[1] = originalSH[originalIndices[tetraIndices2[bestTetraIndex + 1]]];
                                corners[2] = originalSH[originalIndices[tetraIndices2[bestTetraIndex + 2]]];
                                corners[3] = originalSH[originalIndices[tetraIndices2[bestTetraIndex + 3]]];

                                // Manually interpolating the Spherical Harmonic, we generate a new one and compare with what was baked.
                                interpProbe = (corners[0] * coordinates[0]) +
                                    (corners[1] * coordinates[1]) +
                                    (corners[2] * coordinates[2]) +
                                    (corners[3] * coordinates[3]);

                                var error = CompareSH(interpProbe, originalSH[i]);
                                if (((errorTolerance != -1.0f) && (error < errorTolerance)) || float.IsNaN(error)
                                ) // always delete NaN errors, it means light probes are garbage
                                {
                                    //									Debug.Log("Error tolerance is reasonable for probe " + i + " Err: " + error);
                                    // Note, if the SH we had originally is almost the same as the one we can generate using corner points and interpolation, let's throw it out.
                                    // lock all the verts
                                    toRemove.Add(i);
                                    foreach (var vIndex in originalIndices
                                    ) // originalIndices already excludes the point we are removing (i), so we just add the whole array to the lock set for this pass
                                    {
                                        locked.Add(
                                            vIndex
                                        ); // lock everything related to this set of tetrahedrons.  None of them can be removed.
                                    }

                                    totalRemoved++;
                                }
                                else // initial pass always just tells us about errors, additional passes tell about errors of probes we don't delete
                                {
                                    perProbeError.Add(error);
                                }
                            }
                        }
                    }

                    // Recopy all the remaining probe points back to probePositions array, but keep them in WS
                    probePositions = new Vector3[p.Count - toRemove.Count];
                    var positionIndex = 0;
                    for (var i = 0; i < p.Count; i++)
                    {
                        if (toRemove.Contains(i) == false)
                        {
                            probePositions[positionIndex] = p[i];
                            positionIndex++;
                        }
                    }

                    if ((perProbeError.Count == 0) ||
                        (probePositions.Length <= probeBudget) ||
                        ((errorTolerance != -1.0f) && (toRemove.Count == 0))
                    ) // keep optimizing until we stop removing points.
                    {
                        break;
                    }

                    // do analysis of the data and pick an error tolerance that will remove almost exactly the right number of probes
                    perProbeError.Sort();

                    // Take half the probes away at a time
                    var errorIndex = Mathf.Clamp(perProbeError.Count - probeBudget, 0, perProbeError.Count - 1);
                    errorTolerance = (perProbeError[errorIndex] + perProbeError[0]) * 0.5f;
                }

                // Move all world space points back to local space and assign to light probe group.
                EditorUtility.DisplayProgressBar(
                    "AutoProbe: Optimizing Light Probes (" + gameObject.name + ")",
                    "Moving to object space",
                    0.0f
                );
                for (var i = 0; i < probePositions.Length; i++)
                {
                    probePositions[i] = transform.InverseTransformPoint(probePositions[i]);
                    if ((i % logStep) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            "AutoProbe: Optimizing Light Probes (" + gameObject.name + ")",
                            "Moving to world space",
                            i / (float) probePositions.Length
                        );
                    }
                }

                EditorUtility.DisplayProgressBar(
                    "AutoProbe: Optimizing Light Probes (" + gameObject.name + ")",
                    "Moving to world space",
                    1.0f
                );

                // force the update of the inspector
                {
                    lpg = Undo.AddComponent<LightProbeGroup>(gameObject);
                    Undo.RecordObject(lpg, "Optimize Probes");
                    lpg.probePositions = probePositions;
                    lpg.dering = lpgDering;
                }
                Undo.CollapseUndoOperations(Undo.GetCurrentGroup());
                Undo.SetCurrentGroupName("Optimize Probes");
                return totalRemoved;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            return 0;
        }

        private void FilterPointsFromTerrain(AppaList<Vector3> p)
        {
            var terrains = Terrain.activeTerrains;

            for (var i = p.Count - 1; i >= 0; i--)
            {            
                if (!allowAboveTerrain && !allowBelowTerrain)
                {
                    if (!terrains.IsPositionGroundedOnTerrain(p[i]))
                    {
                        p.RemoveAt(i);
                    }
                }
                else
                {
                    if (!allowAboveTerrain && terrains.IsPositionAboveTerrain(p[i]))
                    {
                        p.RemoveAt(i);
                        continue;
                    }

                    if (!allowBelowTerrain && terrains.IsPositionBelowTerrain(p[i]))
                    {
                        p.RemoveAt(i);
                    }
                }
            }
        }

#endif
    }
}
