#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for <c>Gpu.ForUnroll</c> and <c>Gpu.GridStrideLoop</c> called with named arguments in another
/// order than the parameters: each argument binds to the parameter it names, as in C#.
/// </summary>
public static partial class MacroNamedArgumentDifferentialKernels
{
    public static readonly string[] ForUnrollColumns = ["reordered", "body_first", "context_last"];

    public static readonly string[] BarrierColumns = ["reordered", "context_last"];

    [GpuKernel]
    public static void ForUnrollNamedArgumentsKernel(int[] xs, int[] ys, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int y = ys[tid] & 0x3FF;
        int o = tid * 3;

        int reordered = 0;
        Gpu.ForUnroll(count: 3, start: 1, body: i => { reordered += (x + i) * (i + 2); });
        r[o + 0] = reordered;

        int bodyFirst = 0;
        Gpu.ForUnroll(body: i => { bodyFirst += (y * i) + 5; }, start: -1, count: 4);
        r[o + 1] = bodyFirst;

        var ctx = new DiffUnrollContext { Base = x & 15, Acc = 1 };
        Gpu.ForUnroll(
            body: (ref DiffUnrollContext c, int i) => { c.Acc = (c.Acc * 3) + i + c.Base; },
            count: 3,
            ctx: ref ctx,
            start: 1);
        r[o + 2] = ctx.Acc;
    }

    [GpuKernel]
    public static void GridStrideLoopNamedArgumentsKernel(int[] xs, int[] r, int bias)
    {
        Gpu.GridStrideLoop(body: i => { r[i * 2] = (xs[i] & 0xFFFF) * 3 + 1; }, maxBounds: xs.Length);

        int offset = bias;
        Gpu.GridStrideLoop(
            body: (ref int s, int i) => { r[(i * 2) + 1] = (xs[i] & 0xFFFF) + s; },
            maxBounds: xs.Length,
            ctx: ref offset);
    }

    // The barriers make the lowering expand the loops before the state machine splits them
    [GpuKernel]
    public static void ForUnrollNamedArgumentsWithBarrierKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        int x = xs[tid] & 0x3FF;
        int o = tid * 2;

        int reordered = 0;
        Gpu.ForUnroll(count: 3, start: 1, body: i =>
        {
            reordered += (x + i) * (i + 2);
            Gpu.SyncThreads();
        });
        r[o + 0] = reordered;

        var ctx = new DiffUnrollContext { Base = x & 15, Acc = 1 };
        Gpu.ForUnroll(
            count: 2,
            body: (ref DiffUnrollContext c, int i) =>
            {
                c.Acc = (c.Acc * 3) + i + c.Base;
                Gpu.SyncThreads();
            },
            start: 2,
            ctx: ref ctx);
        r[o + 1] = ctx.Acc;
    }
}
