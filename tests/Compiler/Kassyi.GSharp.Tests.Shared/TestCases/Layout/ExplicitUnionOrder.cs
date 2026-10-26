using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

namespace ExplicitUnionOrderTest
{
    // I is declared first, but L is larger: L must be the first member of the device union
    [StructLayout(LayoutKind.Explicit)]
    public struct IntLongUnion
    {
        [FieldOffset(0)]
        public int I;

        [FieldOffset(0)]
        public long L;
    }

    public static partial class UnionOrderKernels
    {
        [GpuKernel]
        public static void ReadKernel(IntLongUnion u, long[] o)
        {
            o[0] = u.L;
        }
    }
}
