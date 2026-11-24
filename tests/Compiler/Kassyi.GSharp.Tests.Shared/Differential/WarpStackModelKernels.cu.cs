using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Kernels whose lanes meet at the same instance of a warp collective by how often each reached it on the same static
/// call path: the same call site of a device function reached in different iterations, the copies of a
/// <see cref="Gpu.ForUnroll"/> body, and visits that one block makes more than the other; and the reconvergence regions
/// around <see cref="Warp.ActiveMask"/>: a lane of one block that ends inside a branch, and a continue past a local of
/// a loop body. The state machine and the stack models count the visits and place the branches alike.
/// </summary>
[SkipSnapshotConsistency("Verified by WarpStackModelTests on every path, under both CPU models")]
public static partial class WarpStackModelKernels
{
    // A shuffle of the two lanes of the mask, reached only when take holds
    [GpuDeviceFunction]
    public static int MaybeShuffle(int v, bool take)
    {
        if (take)
        {
            v += Warp.ShuffleXorSync(0x3u, v, 1);
        }

        return v;
    }

    // Lane 0 reaches the shuffle in the first call, lane 1 in the second call of the same call site
    [GpuKernel]
    public static void SkewedCallsKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        for (int k = 0; k < 2; k++)
        {
            v = MaybeShuffle(v, (t == 0) == (k == 0));
        }

        output[t] = v;
    }

    // The same skew in the two copies of a Gpu.ForUnroll body, which share the site of the one shuffle
    [GpuKernel]
    public static void ForUnrollSkewedShuffleKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        Gpu.ForUnroll(0, 2, k =>
        {
            if ((t == 0) == (k == 0))
            {
                v += Warp.ShuffleXorSync(0x3u, v, 1);
            }
        });
        output[t] = v;
    }

    // The lanes of mask read lane source
    [GpuDeviceFunction]
    public static int ReadLane(int v, uint mask, int source) => Warp.ShuffleSync(mask, v, source);

    // Lane 0 alone reads itself at one call site, then both lanes read each other at another call site of the same
    // function: each call site has a frame of its own, so the second call is the first visit of both lanes there. With
    // the two frames at one offset, lane 1 would meet the first call of lane 0
    [GpuKernel]
    public static void CallSiteFramesKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int v = input[t];
        if (t == 0)
        {
            v = ReadLane(v, 0x1u, 0) + 1;
        }

        v = ReadLane(v, 0x3u, t ^ 1);
        output[t] = v;
    }

    // Both lanes of each block read each other, then lane 0 of block 0 alone reads itself at the same site once more,
    // so that block 0 ends with counts that differ between its lanes; block 1 starts from counts of its own
    [GpuKernel]
    public static void StaleVisitsKernel(int[] input, int[] output)
    {
        int t = ThreadIndex.X;
        int i = BlockIndex.X * BlockDimension.X + t;
        int visits = BlockIndex.X == 0 && t == 0 ? 2 : 1;
        int v = input[i];
        for (int k = 0; k < visits; k++)
        {
            uint mask = k == 0 ? 0x3u : 0x1u;
            int source = k == 0 ? t ^ 1 : t;
            v = Warp.ShuffleSync(mask, v, source) + 1;
        }

        output[i] = v;
    }

    // Lane 0 of block 0 ends inside a thread-dependent branch that reaches Warp.ActiveMask(), so it never leaves the
    // branch; the lanes of block 1 enter the same branch with nothing of block 0 left
    [GpuKernel]
    public static void EndInsideBranchKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int b = BlockIndex.X;
        int inside = 0;
        if (t < 2)
        {
            if (b == 0 && t == 0)
            {
                return;
            }

            inside = (int)Warp.ActiveMask();
        }

        output[b * 8 + t] = inside;
        output[b * 8 + 4 + t] = (int)Warp.ActiveMask();
    }

    // The lanes whose bit k is clear continue before a local of the loop body is declared: on the CPU stack model the
    // continue is a jump past the local to the end of the body, where the lanes of the iteration meet
    [GpuKernel]
    public static void ContinuePastLocalKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int sum = 0;
        for (int k = 0; k < 2; k++)
        {
            if (((t >> k) & 1) == 0)
            {
                sum += (int)Warp.ActiveMask();
                continue;
            }

            int scaled = (int)Warp.ActiveMask() * (k + 1);
            sum += scaled;
        }

        output[t] = sum;
    }

    // The lanes whose t + i is even continue before a nullable local that a target-typed ?: initializes from
    // Warp.ActiveMask(): the continue of the stack form jumps past the nullable local and the temporary of the ?:
    [GpuKernel]
    public static void ContinuePastNullableKernel(int[] output)
    {
        int t = ThreadIndex.X;
        for (int i = 0; i < 2; i++)
        {
            if ((t + i) % 2 == 0)
            {
                continue;
            }

            uint? v = t > 3 ? Warp.ActiveMask() : null;
            output[t] += (int)(v ?? 9u);
        }
    }

    // The lanes whose t + i is even continue before a using declaration: the native stack model keeps the kernel on the
    // state machine, as the C++ form of the using would put the end of the loop body inside a try
    [GpuKernel]
    public static void ContinuePastUsingKernel(int[] output)
    {
        int t = ThreadIndex.X;
        for (int i = 0; i < 2; i++)
        {
            if ((t + i) % 2 == 0)
            {
                continue;
            }

            using var guard = new WarpStackModelGuard { V = t };
            output[t] += (int)Warp.ActiveMask() + guard.V;
        }
    }
}

/// <summary>A resource whose <c>Dispose</c> does nothing observable, for a using declaration in a kernel.</summary>
public struct WarpStackModelGuard : IDisposable
{
    public int V;

    public void Dispose() => V = 0;
}
