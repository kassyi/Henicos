// ReSharper disable All

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for library consumers")]
[SuppressMessage("GSharp", "GSH1002",
    Justification = "Lambda capture limitations in source generation prevent passing context by reference.")]
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
}


