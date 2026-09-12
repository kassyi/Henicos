// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// ReSharper disable once UnusedType.Global -- Public API exposed for library consumers
// ReSharper disable once UnusedMember.Global -- Public API exposed for library consumers
#pragma warning disable GSH0014 // Justification: Lambda capture limitations in source generation prevent passing context by reference.
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntFermatKernels
{
    [GpuDeviceFunction]
    public static void Device_Unpack16([GpuConst] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords)
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
    public static void Device_Transpose_32x32([GpuConst] uint[] idata, uint[] odata, int width, int height)
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
    public static void Device_ShiftLeft_SingleThread([GpuConst] uint[] src, uint[] dst, int shiftBits, int numWords)
    {
        var wordShift = shiftBits / 32;
        var bitShift = shiftBits % 32;
        for (int i = 0; i < numWords; i++)
        {
            uint val = 0;
            if (i - wordShift >= 0)
            {
                val = src[i - wordShift] << bitShift;
                if (bitShift > 0 && i - wordShift - 1 >= 0)
                {
                    val |= src[i - wordShift - 1] >> (32 - bitShift);
                }
            }

            dst[i] = val;
        }
    }

    [GpuDeviceFunction]
    public static void Device_AddSub_SingleThread([GpuConst] uint[] a, [GpuConst] uint[] b, uint[] result, int numWords,
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
    public static void Kernel_FermatReduce_Mega(
        [GpuConst] uint[] valueT,
        uint[] mulResult,
        uint[] quotient,
        uint[] remainder,
        uint[] temp,
        [GpuConst] uint[] remainderMask,
        int numWords,
        int outNumWords,
        int totalShiftBits,
        int exponent)
    {
        Gpu.SingleThread(() =>
        {
            var wordShift = totalShiftBits / 32;
            var bitShift = totalShiftBits % 32;

            for (int i = 0; i < outNumWords; i++)
            {
                uint val = 0;
                if (i + wordShift < numWords * 2)
                {
                    val = mulResult[i + wordShift] >> bitShift;
                    if (bitShift > 0 && i + wordShift + 1 < numWords * 2)
                    {
                        val |= mulResult[i + wordShift + 1] << (32 - bitShift);
                    }
                }

                quotient[i] = val;
            }

            for (int i = 0; i < outNumWords; i++)
            {
                remainder[i] = mulResult[i] & remainderMask[i];
            }

            Device_ShiftLeft_SingleThread(quotient, temp, exponent, outNumWords);
            Device_AddSub_SingleThread(remainder, temp, remainder, outNumWords, 1);

            Device_ShiftLeft_SingleThread(quotient, temp, 1, outNumWords);
            Device_AddSub_SingleThread(remainder, temp, remainder, outNumWords, -1);
            Device_AddSub_SingleThread(remainder, quotient, remainder, outNumWords, -1);

            Device_ShiftLeft_SingleThread(quotient, temp, 3, outNumWords);
            Device_AddSub_SingleThread(remainder, temp, valueT, outNumWords, -1);
        });
    }
}


