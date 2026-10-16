namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

using System.Runtime.CompilerServices;
using Core;

// Reference/Inspiration: stillwater-sc/universal (Mathematical concepts)

[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigFloatKernels
{
    public struct GpuBigFloatAddContext
    {
        [GpuReadOnly]
        public uint[] LargerSig;

        public int LargerWords;

        [GpuReadOnly]
        public uint[] SmallerSig;

        public int SmallerWords;
        public uint[] ResultSig;
        public int NumWords;
        public int WordShift;
        public int BitShift;
        public int SignScale;
    }

    [GpuKernel]
    public static void FloatAddAlignedKernel(GpuBigFloatAddContext ctx)
    {
        GpuBigIntKernels.DeviceAddSubAlignedParallel(
            ctx.LargerSig,
            ctx.LargerWords,
            ctx.SmallerSig,
            ctx.SmallerWords,
            ctx.ResultSig,
            ctx.NumWords,
            ctx.WordShift,
            ctx.BitShift,
            ctx.SignScale,
            syncThreads: false);
    }

    [GpuKernel]
    public static void FloatNormalizeKernel(
        [GpuReadOnly] uint[] valueSig, int valueWords,
        uint[] resultSig, int resultWords,
        int wordShift, int bitShift)
    {
        var tid = ThreadIndex.X;
        var bDim = BlockDimension.X;

        for (int offset = 0; offset < resultWords; offset += bDim)
        {
            var idx = offset + tid;
            var valid = idx < resultWords;

            if (valid)
            {
                var srcIdxHi = idx - wordShift;
                var srcIdxLo = srcIdxHi - 1;

                var hi = (srcIdxHi >= 0 && srcIdxHi < valueWords) ? valueSig[srcIdxHi] : 0u;
                var lo = (srcIdxLo >= 0 && srcIdxLo < valueWords) ? valueSig[srcIdxLo] : 0u;

                if (bitShift == 0)
                {
                    resultSig[idx] = hi;
                }
                else
                {
                    resultSig[idx] = Gpu.FunnelShiftLeft(lo, hi, (uint)bitShift);
                }
            }
        }
    }
}
