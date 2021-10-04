#if UNITY_EDITOR
using UnityEngine;

namespace Appalachia.Lighting.Occlusion
{
    public partial class OcclusionProbes : MonoBehaviour
    {
        private static readonly Vector3i[] s_Offsets =
        {
            new(1, 0, 0),
            new(-1, 0, 0),
            new(0, 1, 0),
            new(0, -1, 0),
            new(0, 0, 1),
            new(0, 0, -1)
        };

        private static int IndexAt(Vector3i pos, Vector3i count)
        {
            return pos.x + (pos.y * count.x) + (pos.z * count.x * count.y);
        }

        private static Vector4 Sample(Vector4[] data, Vector3i pos, Vector3i count)
        {
            if ((pos.x < 0) ||
                (pos.y < 0) ||
                (pos.z < 0) ||
                (pos.x >= count.x) ||
                (pos.y >= count.y) ||
                (pos.z >= count.z))
            {
                return new Vector4(0, 0, 0, 1);
            }

            return data[IndexAt(pos, count)];
        }

        private static bool OverwriteInvalidProbe(
            Vector4[] dataSrc,
            Vector4[] dataDst,
            Vector3i pos,
            Vector3i count,
            float backfaceTolerance)
        {
            var center = Sample(dataSrc, pos, count);

            var centerIndex = IndexAt(pos, count);
            dataDst[centerIndex] = center;

            if (center.w <= backfaceTolerance)
            {
                return true;
            }

            var weights = 0;
            var result = Vector4.zero;

            foreach (var offset in s_Offsets)
            {
                var samplePos = pos + offset;
                var sample = Sample(dataSrc, samplePos, count);
                if (sample.w > backfaceTolerance)

                    // invalid sample, don't use
                {
                    continue;
                }

                result += sample;
                weights++;
            }

            if (weights > 0)
            {
                dataDst[centerIndex] = result / weights;
                return true;
            }

            // Haven't managed to overwrite an invalid probe
            return false;
        }

        private static void DilateOverInvalidProbes(
            ref Vector4[] data,
            Vector3i count,
            float backfaceTolerance,
            int dilateIterations)
        {
            if (dilateIterations == 0)
            {
                return;
            }

            var dataBis = new Vector4[data.Length];

            for (var i = 0; i < dilateIterations; ++i)
            {
                var invalidProbesRemaining = false;

                for (var z = 0; z < count.z; ++z)
                for (var y = 0; y < count.y; ++y)
                for (var x = 0; x < count.x; ++x)
                {
                    invalidProbesRemaining |= !OverwriteInvalidProbe(
                        data,
                        dataBis,
                        new Vector3i(x, y, z),
                        count,
                        backfaceTolerance
                    );
                }

                // Swap buffers
                var dataTemp = data;
                data = dataBis;
                dataBis = dataTemp;

                if (!invalidProbesRemaining)
                {
                    break;
                }
            }
        }

        private static void ClampEdgesToWhite(ref Vector4[] data, Vector3i count)
        {
            var pos = new Vector3i();
            var white = new Vector4(1, 1, 1, 0);
            var maxz = count.z - 1;
            for (var x = 0; x < count.x; x++)
            for (var y = 0; y < count.y; y++)
            {
                pos.x = x;
                pos.y = y;
                pos.z = 0;
                data[IndexAt(pos, count)] = white;
                pos.z = maxz;
                data[IndexAt(pos, count)] = white;
            }

            var maxx = count.x - 1;
            for (var z = 0; z < count.z; z++)
            for (var y = 0; y < count.y; y++)
            {
                pos.z = z;
                pos.y = y;
                pos.x = 0;
                data[IndexAt(pos, count)] = white;
                pos.x = maxx;
                data[IndexAt(pos, count)] = white;
            }
        }

        private struct Vector3i
        {
            public int x;
            public int y;
            public int z;

            public Vector3i(int x, int y, int z)
            {
                this.x = x;
                this.y = y;
                this.z = z;
            }

            public static Vector3i operator +(Vector3i v1, Vector3i v2)
            {
                return new(v1.x + v2.x, v1.y + v2.y, v1.z + v2.z);
            }
        }
    }
}
#endif
