using System.Numerics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

public static partial class BitOpsNuintProbeKernels
{
    [GpuKernel]
    public static void ProbeKernel(ulong[] u64, int[] offsets, ulong[] r)
    {
        nuint vn = (nuint)u64[0];
        int s = offsets[0];
        r[0] = (ulong)BitOperations.LeadingZeroCount(vn);
        r[1] = (ulong)BitOperations.TrailingZeroCount(vn);
        r[2] = (ulong)BitOperations.PopCount(vn);
        r[3] = (ulong)BitOperations.Log2(vn);
        r[4] = BitOperations.RotateLeft(vn, s);
        r[5] = BitOperations.RotateRight(vn, s);
    }
}
