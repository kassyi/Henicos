#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Cuda.Differential;

public struct CudaSwitchScopeGuard : IDisposable
{
    public void Dispose()
    {
    }
}

[SkipSnapshotConsistency("CUDA-only unroll hint test for Warp.ActiveMask inside a loop with thread-variant trip count")]
public static partial class WarpActiveMaskCudaDifferentialKernels
{
    // Counted-shape for loop with a thread-variant trip count and an unroll hint; CUDA emits the iteration key
    // reset before Gpu.Hint.Unroll(4) so nvcc sees the pragma directly above the loop header.
    // Slots: 0..14 per iteration index i (for u == 0); 15 unused; 16 after the loop.
    [GpuKernel]
    public static void ForVariantTripUnrollFourKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        Gpu.Hint.Unroll(4);
        for (int i = 0; i < u + 1 + (t & 7) * 2; i++)
        {
            result[t + 32 * i] = Warp.ActiveMask();
        }

        result[t + 512] = Warp.ActiveMask();
    }

    // A uniform for loop whose body contains a conditional access (?[]) on a thread-variant receiver without any other
    // branch construct inside the loop. Slots: 0 i=0; 1 i=1
    [GpuKernel]
    public static void ConditionalAccessInUniformLoopKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint[]? a = (t & 1) == 0 ? result : null;
        uint[]? b = (t & 2) == 0 ? result : null;
        for (int i = 0; i < 2; i++)
        {
            uint[]? row = a;
            if (i == 1)
            {
                row = b;
            }

            row?[t + 32 * i] = Warp.ActiveMask();
        }
    }

    // A switch section with a self-goto-case cycle crossing using var (u == 0) and fixed (u == 1) scopes on CUDA.
    // Slots: u == 0 -> 0..1 header, 2 post-goto in using, 3 after using; u == 1 -> 0..1 header, 2 post-goto in fixed
    [GpuKernel]
    public static unsafe void SwitchGotoCaseCrossingUsingAndFixedKernel(uint[] result, int u)
    {
        int t = ThreadIndex.X;
        int k = 0;
        switch (u)
        {
            case 0:
            {
                using var gd = new CudaSwitchScopeGuard();
                result[t + 32 * k] = Warp.ActiveMask();
                if ((t & 1) == 0 && k++ < 1)
                {
                    goto case 0;
                }

                result[t + 64] = Warp.ActiveMask();
            }

                result[t + 96] = Warp.ActiveMask();
                break;
            case 1:
                fixed (uint* p = result)
                {
                    p[t + 32 * k] = Warp.ActiveMask();
                    if ((t & 2) == 0 && k++ < 1)
                    {
                        goto case 1;
                    }

                    p[t + 64] = Warp.ActiveMask();
                }

                break;
        }
    }

    // A counted for loop where each lane breaks on its own iteration and writes ActiveMask through ?[] inside the break
    // block. Slots: 0..3 loop header; 4 break block (0x11111111 << (lane & 3))
    [GpuKernel]
    public static void BreakBlockConditionalAccessKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint[]? row = result;
        for (int i = 0; i < 4; i++)
        {
            row?[t + 32 * i] = Warp.ActiveMask();
            if (i == (t & 3))
            {
                row?[t + 128] = Warp.ActiveMask();
                break;
            }
        }
    }

    // A for loop with a thread-variant start value whose ActiveMask call is the value assigned through ?[].
    // Slots: 0 i=0 (even lanes, 0xFFFFFFFF); 1 i=1 (odd lanes on trip 1 0xFFFFFFFF, even lanes on trip 2 0x55555555)
    [GpuKernel]
    public static void DivergentForConditionalAccessKernel(uint[] result)
    {
        int t = ThreadIndex.X;
        uint[]? row = result;
        for (int i = t & 1; i < 2; i++)
        {
            row?[t + 32 * i] = Warp.ActiveMask();
        }
    }
}
