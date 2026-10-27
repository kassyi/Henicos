using Kassyi.GSharp.Core;
using OverloadLibrary;

namespace DeviceImportCase
{
    public static partial class OverloadKernels
    {
        [GpuKernel]
        public static void PickKernel(int[] ints, float[] floats)
        {
            ints[ThreadIndex.X] = LibPick.Pick(ints[ThreadIndex.X]);
            floats[ThreadIndex.X] = LibPick.Pick(floats[ThreadIndex.X]);
        }
    }
}
