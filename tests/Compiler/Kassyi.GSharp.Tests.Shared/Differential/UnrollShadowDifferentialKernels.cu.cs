#nullable enable
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Differential kernels for a <c>Gpu.ForUnroll</c> body that declares a local function or lambda whose parameter has the
/// name of the loop or context parameter: the nested parameter is its own variable, which the unrolled index or the
/// context argument does not replace.
/// </summary>
public static partial class UnrollShadowDifferentialKernels
{
    public static readonly string[] Columns =
        ["local_function_0", "local_function_1", "lambda_0", "lambda_1", "context"];

    [GpuKernel]
    public static void UnrollShadowKernel(int[] xs, int[] r)
    {
        int tid = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
        if (tid >= xs.Length) return;
        int x = xs[tid] & 0x3FF;
        int o = tid * 5;

        Gpu.ForUnroll(0, 2, i =>
        {
            int Square(int i) => i * i;
            r[o + i] = Square(x + 3) + i;
        });

        Gpu.ForUnroll(0, 2, i =>
        {
            Func<int, int> twice = i => i * 2;
            r[o + 2 + i] = twice(x + 5) + i;
        });

        var ctx = new DiffUnrollContext { Base = x & 15, Acc = 1 };
        Gpu.ForUnroll(ref ctx, 1, 2, (ref DiffUnrollContext c, int i) =>
        {
            int Read(DiffUnrollContext c) => c.Base * 7;
            c.Acc = (c.Acc * 3) + Read(new DiffUnrollContext { Base = i + 1 }) + c.Base;
        });
        r[o + 4] = ctx.Acc;
    }
}
