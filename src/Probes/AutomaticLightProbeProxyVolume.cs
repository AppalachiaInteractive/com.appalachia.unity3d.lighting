using Appalachia.Core.Collections.Implementations.Lists;
using Appalachia.Core.Collections.NonSerialized;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

#if UNITY_EDITOR

#endif

namespace Appalachia.Lighting.Probes
{
    [ExecuteAlways]
    public class AutomaticLightProbeProxyVolume : MonoBehaviour
    {
#if UNITY_EDITOR

        [HideInInspector] public LightProbeProxyVolume volume;

        [BoxGroup("Volume Lock")]
        public bool locked;

        [DelayedProperty]
        [OnValueChanged(nameof(OnNameChanged))]
        public string volumeName;

        [Tooltip(
            "The volume will encompass these objects."
        )]
        [SceneObjectsOnly]
        public AppaList_GameObject encompassing = new AppaList_GameObject(12);

        [Tooltip("Should child objects be encompassed?")]
        [OnValueChanged(nameof(UpdateVolume))]
        public bool includeChildren = true;

        [Tooltip("Typical spacing between points.")]
        [PropertyRange(0.01f, 1.0f)]
        [OnValueChanged(nameof(UpdateVolume))]
        public float density = 0.1f;

        [Tooltip("Expands or contracts bounds.")]
        [PropertyRange(0.5f, 2.0f)]
        [OnValueChanged(nameof(UpdateVolume))]
        public float multiplier = 1.1f;

        [Tooltip("Should the probe be placed in the center of the cell or the corner?")]
        [OnValueChanged(nameof(UpdateVolume))]
        public LightProbeProxyVolume.ProbePositionMode positionMode;

        [Tooltip("What should the quality of the spherical harmonics be?")]
        [OnValueChanged(nameof(UpdateVolume))]
        public LightProbeProxyVolume.QualityMode qualityMode;

        [BoxGroup("Initial Placement")]
        [LabelText("Layer Mask")]
        [Tooltip(
            "Choose the layers that you want rays to consider for collision.  Useful if dynamic objects are mixed into your scene (in a separate layer) but aren't supposed to block light."
        )]
        [OnValueChanged(nameof(UpdateVolume))]
        public LayerMask cullingMask = ~0;

        protected string LightProbeGroupName => "_LIGHT_PROBE_PROXY_VOLUME {0}";

        private void OnNameChanged()
        {
            gameObject.name = string.Format(LightProbeGroupName, volumeName);
        }
        
        public void Awake()
        {
            Validate();
        }

        private void Validate()
        {
        
            gameObject.name = string.Format(LightProbeGroupName, volumeName);
            
            if (volume == null)
            {
                volume = GetComponent<LightProbeProxyVolume>();
            }

            if (volume == null)
            {
                volume = gameObject.AddComponent<LightProbeProxyVolume>();
            }

            var t = transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
        }
        
        public void UpdateVolume()
        {
            Validate();

            var boundsCollection = new NonSerializedList<(GameObject go, Bounds bounds, MeshRenderer mr)>(128);

            for (var i = encompassing.Count - 1; i >= 0; i--)
            {
                var go = encompassing[i];

                if (go == null)
                {
                    encompassing.RemoveAt(i);
                }

                var mr = go.GetComponents<MeshRenderer>();
                var mrc = go.GetComponentsInChildren<MeshRenderer>();
                var mc = go.GetComponents<Collider>();
                var mcc = go.GetComponentsInChildren<Collider>();

                for (var index = 0; index < mr.Length; index++)
                {
                    var o = mr[index];
                    boundsCollection.Add((o.gameObject, o.bounds, o));
                }

                for (var index = 0; index < mrc.Length; index++)
                {
                    var o = mrc[index];
                    boundsCollection.Add((o.gameObject, o.bounds, o));
                }

                for (var index = 0; index < mc.Length; index++)
                {
                    var o = mc[index];
                    boundsCollection.Add((o.gameObject, o.bounds, null));
                }

                for (var index = 0; index < mcc.Length; index++)
                {
                    var o = mcc[index];
                    boundsCollection.Add((o.gameObject, o.bounds, null));
                }
            }


            var proxyBounds = new Bounds();

            for (var index = 0; index < boundsCollection.Count; index++)
            {
                var r = boundsCollection[index];
                if (cullingMask != (cullingMask | (1 << r.go.layer)))
                {
                    continue;
                }

                var bounds = r.bounds;

                proxyBounds.Encapsulate(bounds);

                if (r.mr != null)
                {
                    var use = r.mr.receiveGI != ReceiveGI.Lightmaps;

                    if (!use)
                    {
                        continue;
                    }

                    if (r.mr.lightProbeProxyVolumeOverride != null &&
                        r.mr.lightProbeProxyVolumeOverride != volume.gameObject)
                    {
                        continue;
                    }

                    GameObjectUtility.SetStaticEditorFlags(r.go, StaticEditorFlags.ContributeGI);
                    r.mr.receiveGI = ReceiveGI.LightProbes;
                    r.mr.lightProbeUsage = LightProbeUsage.UseProxyVolume;
                    r.mr.lightProbeProxyVolumeOverride = volume.gameObject;
                }
            }

            proxyBounds.size *= multiplier;

            volume.boundingBoxMode = LightProbeProxyVolume.BoundingBoxMode.Custom;
            volume.originCustom = transform.InverseTransformPoint(proxyBounds.center);
            volume.sizeCustom = proxyBounds.size;
            volume.refreshMode = LightProbeProxyVolume.RefreshMode.Automatic;
            
            volume.probeDensity = density;
            volume.probePositionMode = positionMode;
            volume.qualityMode = qualityMode;
        }

#endif
    }
}
