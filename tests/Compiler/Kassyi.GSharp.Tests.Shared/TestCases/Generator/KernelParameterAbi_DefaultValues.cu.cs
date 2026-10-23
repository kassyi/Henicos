#nullable enable
using Kassyi.GSharp.Core;

namespace AbiTest.DefaultValues
{
    // Every kind of default value literal; the overload makes the typed Ptr wrapper take the optional buffer
    public static partial class DefaultKernels
    {
        [GpuKernel]
        [SkipSnapshotConsistency("Generator case")]
        public static void ScaleKernel(float[] data, bool enabled = true, bool inverted = false, float scale = 1.5f,
            double offset = 0.25, int count = 3, string label = "a\"b", float[]? extra = null)
        {
            int i = ThreadIndex.X;
            data[i] = data[i] * scale;
        }

        [GpuKernel]
        public static void ScaleKernel(int[] data, int[]? extra = null)
        {
            int i = ThreadIndex.X;
            data[i] = data[i] * 2;
        }
    }
}
