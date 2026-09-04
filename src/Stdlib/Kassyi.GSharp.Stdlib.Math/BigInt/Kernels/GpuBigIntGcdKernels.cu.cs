// ReSharper disable All
namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
[Kassyi.GSharp.Core.SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntGcdKernels
{
    [GpuKernel]
    public static void ApplyMatrix2x2Kernel(uint[] a, uint[] b, uint[] dstA, uint[] dstB, long u0, long v0, long u1, long v1, int numWords)
    {
        Gpu.SingleThread(() =>
        {
            long carryA = 0;
            long carryB = 0;
            for (int i = 0; i < numWords; i++)
            {
                long valA = a[i];
                long valB = b[i];

                var nextA = valA * u0 + valB * v0 + carryA;
                var nextB = valA * u1 + valB * v1 + carryB;

                dstA[i] = (uint)(nextA & 0xFFFFFFFF);
                carryA = nextA >> 32;

                dstB[i] = (uint)(nextB & 0xFFFFFFFF);
                carryB = nextB >> 32;
            }
        });
    }

    [GpuDeviceFunction]
    public static bool ComputeLehmerMatrix(ulong x, ulong y, out long u0, out long v0, out long u1, out long v1)
    {
        u0 = 1; v0 = 0;
        u1 = 0; v1 = 1;
        var progress = false;

        while (y != 0)
        {
            if (y + 1 == 0 || x == ~0ul)
            {
                break; // Overflow protection
            }

            var qMin = x / (y + 1);
            var qMax = (x + 1) / y;

            if (qMin != qMax)
            {
                break;
            }

            var q = (long)qMin;
            if (q == 0)
            {
                break;
            }

            var r = x - (ulong)q * y;

            var next_u = u0 - q * u1;
            var next_v = v0 - q * v1;

            if (next_u > 0x7FFFFFFF || next_u < -0x7FFFFFFF ||
                next_v > 0x7FFFFFFF || next_v < -0x7FFFFFFF)
            {
                break;
            }

            x = y;
            y = r;
            u0 = u1;
            v0 = v1;
            u1 = next_u;
            v1 = next_v;
            progress = true;
        }

        return progress;
    }
}

