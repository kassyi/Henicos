// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
#pragma warning disable GSH0014 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntFermatKernels
{
    [GpuDeviceFunction]
    public static void DeviceUnpack16([GpuReadOnly] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords)
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
    }

    [GpuDeviceFunction]
    public static void DeviceTranspose32x32([GpuReadOnly] uint[] idata, uint[] odata, int width, int height)
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

        if (ThreadIndex.X >= 256)
        {
            return;
        }

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

        Gpu.SyncThreads();

        xIndex = by * 32 + tx;
        yIndex = bx * 32 + ty;

        Gpu.ForUnroll(0, 4, iter =>
        {
            var i = iter * 8;
            if (xIndex < height && yIndex + i < width)
            {
                uint val = 0;
                Gpu.InlinePtx($"{val} = s_mem[{tx} * 33 + ({ty} + {i})];");
                odata[(yIndex + i) * height + xIndex] = val;
            }
        });
    }

    [GpuDeviceFunction]
    public static void DeviceShiftLeftParallel([GpuReadOnly] uint[] src, uint[] dst, int shiftBits, int numWords)
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
    }

    [GpuDeviceFunction]
    public static void DeviceAddSubSingleThread([GpuReadOnly] uint[] a, [GpuReadOnly] uint[] b, uint[] result,
        int numWords,
        long sign)
    {
        long carry = 0;
        for (int i = 0; i < numWords; i++)
        {
            long sum = (long)a[i] + sign * b[i] + carry;
            result[i] = (uint)sum;
            carry = sum >> 32;
        }
    }

    [GpuKernel]
    public static void KernelFermatReduceMega(
        [GpuReadOnly] uint[] valueT,
        uint[] mulResult,
        uint[] quotient,
        uint[] remainder,
        uint[] temp,
        [GpuReadOnly] uint[] remainderMask,
        int numWords,
        int outNumWords,
        int totalShiftBits,
        int exponent)
    {
        var wordShift = totalShiftBits / 32;
        var bitShift = totalShiftBits % 32;

        Gpu.GridStrideLoop(outNumWords, i =>
        {
            var srcIdx0 = i + wordShift;
            var srcIdx1 = srcIdx0 + 1;

            var safeIdx0 = srcIdx0 < 0 ? 0 : (srcIdx0 >= numWords * 2 ? numWords * 2 - 1 : srcIdx0);
            var safeIdx1 = srcIdx1 < 0 ? 0 : (srcIdx1 >= numWords * 2 ? numWords * 2 - 1 : srcIdx1);

            uint w0 = mulResult[safeIdx0];
            uint w1 = mulResult[safeIdx1];

            uint mask0 = srcIdx0 >= 0 && srcIdx0 < numWords * 2 ? 0xFFFFFFFFu : 0u;
            uint mask1 = srcIdx1 >= 0 && srcIdx1 < numWords * 2 ? 0xFFFFFFFFu : 0u;

            w0 &= mask0;
            w1 &= mask1;

            uint shiftMask = bitShift == 0 ? 0u : 0xFFFFFFFFu;
            quotient[i] = (w0 >> bitShift) | ((w1 << (32 - bitShift)) & shiftMask);

            remainder[i] = mulResult[i] & remainderMask[i];
        });

        Gpu.SyncThreads();

        DeviceShiftLeftParallel(quotient, temp, exponent, outNumWords);
        Gpu.SyncThreads();

        Gpu.SingleThread(() =>
        {
            DeviceAddSubSingleThread(remainder, temp, remainder, outNumWords, 1);
        });
        Gpu.SyncThreads();

        DeviceShiftLeftParallel(quotient, temp, 1, outNumWords);
        Gpu.SyncThreads();

        Gpu.SingleThread(() =>
        {
            DeviceAddSubSingleThread(remainder, temp, remainder, outNumWords, -1);
        });
        Gpu.SyncThreads();

        Gpu.SingleThread(() =>
        {
            DeviceAddSubSingleThread(remainder, quotient, remainder, outNumWords, -1);
        });
        Gpu.SyncThreads();

        DeviceShiftLeftParallel(quotient, temp, 3, outNumWords);
        Gpu.SyncThreads();

        Gpu.SingleThread(() =>
        {
            DeviceAddSubSingleThread(remainder, temp, remainder, outNumWords, -1);
        });
    }
}


