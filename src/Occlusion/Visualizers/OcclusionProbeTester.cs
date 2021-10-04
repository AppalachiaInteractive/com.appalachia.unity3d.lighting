#if UNITY_EDITOR
using Appalachia.Editing.Visualizers.Base;
using Sirenix.OdinInspector;
using UnityEngine;

namespace Appalachia.Lighting.Occlusion.Visualizers
{
    [ExecuteAlways]
    public class OcclusionProbeTester : InstancedIndirectVolumeVisualization
    {
        [OnValueChanged(nameof(Regenerate))]
        public OcclusionProbes probes;

        protected override bool ShouldRegenerate => false;

        protected override bool CanVisualize => probes != null;
        protected override bool CanGenerate => probes != null;

        protected override void PrepareInitialGeneration()
        {
            if (probes == null)
            {
                probes = FindObjectOfType<OcclusionProbes>();
            }
        }

        protected override void PrepareSubsequentGenerations()
        {
            if (probes == null)
            {
                probes = FindObjectOfType<OcclusionProbes>();
            }
        }

        protected override Bounds GetBounds()
        {
            if (probes == null)
            {
                probes = FindObjectOfType<OcclusionProbes>();
            }

            if (probes == null)
            {
                return default;
            }

            var probesTransform = probes.transform;

            var bounds = new Bounds
            {
                center = probesTransform.position,
                size = probesTransform.lossyScale + (Vector3.one * (visualizationSize * 3.0f))
            };

            return bounds;
        }

        protected override void WhenDisabled()
        {
            probes = null;
        }
    }
}

#endif
