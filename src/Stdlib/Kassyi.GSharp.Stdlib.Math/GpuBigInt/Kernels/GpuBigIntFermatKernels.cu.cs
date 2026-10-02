namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
#pragma warning disable GSH0014 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigIntFermatKernels
{
    public struct FermatReduceContext
    {
        public uint[] ValueT;
        public uint[] MulResult;
        public uint[] Quotient;
        public uint[] Remainder;
        public uint[] Temp;
        [GpuReadOnly] public uint[] RemainderMask;
        public int NumWords;
        public int OutNumWords;
        public int TotalShiftBits;
        public int Exponent;
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceUnpack16([GpuReadOnly] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords,
        bool syncThreads = true)
    {
        Gpu.GridStrideLoop(n, i =>
        {
            uint val = 0;
            var wordIdx = i / 2;
            if (wordIdx < numWords)
            {
                var w = dSrc[wordIdx];
                if (i % 2 == 0)
                {
                    val = w & 0xFFFF;
                }
                else
                {
                    val = w >> 16;
                }
            }

            dDst1[i] = val;
            dDst2[i] = val;
        });

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceTranspose32X32([GpuReadOnly] uint[] idata, uint[] odata, int width, int height,
        bool syncThreads = true)
    {
        Gpu.InlinePtx("extern __shared__ uint32_t s_mem[];");

        var gridX = (width + 31) / 32;
        var gridY = (height + 31) / 32;
        if (BlockIndex.X >= gridX * gridY)
        {
            return;
        }

        var bx = BlockIndex.X % gridX;
        var by = BlockIndex.X / gridX;

        if (ThreadIndex.X < 256)
        {
            var tx = ThreadIndex.X % 32;
            var ty = ThreadIndex.X / 32;

            var xIndex = bx * 32 + tx;
            var yIndex = by * 32 + ty;

            Gpu.ForUnroll(0, 4, iter =>
            {
                var i = iter * 8;
                if (xIndex < width && yIndex + i < height)
                {
                    Gpu.InlinePtx($"s_mem[({ty} + {i}) * 33 + {tx}] = {idata[(yIndex + i) * width + xIndex]};");
                }
            });
        }

        Gpu.SyncThreads();

        if (ThreadIndex.X < 256)
        {
            var tx = ThreadIndex.X % 32;
            var ty = ThreadIndex.X / 32;

            var outXIndex = by * 32 + tx;
            var outYIndex = bx * 32 + ty;

            Gpu.ForUnroll(0, 4, iter =>
            {
                var i = iter * 8;
                if (outXIndex >= height || outYIndex + i >= width)
                {
                    return;
                }

                uint val = 0;
                Gpu.InlinePtx($"{val} = s_mem[{tx} * 33 + ({ty} + {i})];");
                odata[(outYIndex + i) * height + outXIndex] = val;
            });
        }

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }

    [GpuDeviceFunction]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeviceShiftLeftParallel([GpuReadOnly] uint[] src, uint[] dst, int shiftBits, int numWords,
        bool syncThreads = true)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        Gpu.GridStrideLoop(numWords, i =>
        {
            var srcIdx0 = i - wordShift;
            var srcIdx1 = srcIdx0 - 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords ? numWords - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords ? numWords - 1 : srcIdx1);

            uint w0 = src[safeIdx0];
            uint w1 = src[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < numWords ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < numWords ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            dst[i] = (w0 << bitShift) | ((w1 >> (32 - bitShift)) & shiftMask);
        });

        if (syncThreads)
        {
            Gpu.SyncThreads();
        }
    }


    [GpuKernel]
    public static void KernelTestBlockAddSubParallel(
        [GpuReadOnly] uint[] a,
        [GpuReadOnly] uint[] b,
        uint[] result,
        int numWords,
        int sign)
    {
        GpuBigIntKernels.DeviceAddSubParallel(a, b, result, numWords, sign);
    }

    [GpuKernel]
    public static void KernelFermatReduceMega(FermatReduceContext ctx)
    {
        var wordShift = ctx.TotalShiftBits / 32;
        var bitShift = ctx.TotalShiftBits % 32;

        Gpu.ForUnroll(0, 3, iter =>
        {
            Gpu.GridStrideLoop(ctx.OutNumWords, i =>
            {
                var srcIdx0 = i + wordShift;
                var srcIdx1 = srcIdx0 + 1;

                var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= ctx.NumWords * 2 ? ctx.NumWords * 2 - 1 : srcIdx0);
                var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= ctx.NumWords * 2 ? ctx.NumWords * 2 - 1 : srcIdx1);

                uint w0 = ctx.MulResult[safeIdx0];
                uint w1 = ctx.MulResult[safeIdx1];

                uint mask0 = srcIdx0 >= 0 && srcIdx0 < ctx.NumWords * 2 ? 0xFFFFFFFFu : 0u;
                uint mask1 = srcIdx1 >= 0 && srcIdx1 < ctx.NumWords * 2 ? 0xFFFFFFFFu : 0u;

                w0 &= mask0;
                w1 &= mask1;

                uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
                ctx.Quotient[i] = (w0 >> bitShift) | ((w1 << (32 - bitShift)) & shiftMask);

                ctx.Remainder[i] = ctx.MulResult[i] & ctx.RemainderMask[i];
            });

            Gpu.SyncThreads();

            DeviceShiftLeftParallel(ctx.Quotient, ctx.Temp, ctx.Exponent, ctx.OutNumWords);
            GpuBigIntKernels.DeviceAddSubParallel(ctx.Remainder, ctx.Temp, ctx.Remainder, ctx.OutNumWords);

            // Subtract Q * 8
            DeviceShiftLeftParallel(ctx.Quotient, ctx.Temp, 3, ctx.OutNumWords);
            GpuBigIntKernels.DeviceAddSubParallel(ctx.Remainder, ctx.Temp, ctx.Remainder, ctx.OutNumWords, sign: -1);

            // Subtract Q * 2
            DeviceShiftLeftParallel(ctx.Quotient, ctx.Temp, 1, ctx.OutNumWords);
            GpuBigIntKernels.DeviceAddSubParallel(ctx.Remainder, ctx.Temp, ctx.Remainder, ctx.OutNumWords, sign: -1);

            // Subtract Q * 1
            GpuBigIntKernels.DeviceAddSubParallel(ctx.Remainder, ctx.Quotient, ctx.Remainder, ctx.OutNumWords, sign: -1);

            if (iter >= 2)
            {
                return;
            }

            Gpu.GridStrideLoop(ctx.OutNumWords, i =>
            {
                ctx.MulResult[i] = ctx.Remainder[i];
            });
            Gpu.SyncThreads();
        });
    }
}


