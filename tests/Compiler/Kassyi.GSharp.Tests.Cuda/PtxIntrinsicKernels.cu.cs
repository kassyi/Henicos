using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda;

[SkipSnapshotConsistency]
public static partial class PtxIntrinsicKernels
{
    // Single thread. results: [0] unsigned mad.wide, [1] signed mad.wide, [2] signed mad.wide at the 64-bit edge.
    [GpuKernel]
    public static void MultiplyAddWideKernel(ulong[] unsignedResult, long[] signedResults)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        unsignedResult[0] = Gpu.MultiplyAddWide(uint.MaxValue, uint.MaxValue, 5ul);
        signedResults[0] = Gpu.MultiplyAddWide(-3, 4, -2L);
        signedResults[1] = Gpu.MultiplyAddWide(int.MinValue, int.MinValue, 0L);
    }

    // Single thread. Three (a, b, c, carry-in) cases; results and carry-outs are stored per case.
    [GpuKernel]
    public static void MultiplyAddWideWithCarryKernel(ulong[] results, ulong[] carries)
    {
        if (ThreadIndex.X != 0)
        {
            return;
        }

        // The product plus c wraps: carry-out comes from the first addition.
        ulong carry = 1ul;
        results[0] = Gpu.MultiplyAddWideWithCarry(uint.MaxValue, uint.MaxValue, 0xFFFFFFFF_00000000ul, ref carry);
        carries[0] = carry;

        // No wrap at all.
        carry = 1ul;
        results[1] = Gpu.MultiplyAddWideWithCarry(2u, 3u, 10ul, ref carry);
        carries[1] = carry;

        // Only the carry-in addition wraps.
        carry = 1ul;
        results[2] = Gpu.MultiplyAddWideWithCarry(1u, 1u, ulong.MaxValue - 1ul, ref carry);
        carries[2] = carry;
    }
}
