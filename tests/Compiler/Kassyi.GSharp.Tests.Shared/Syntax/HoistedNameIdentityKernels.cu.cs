// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels that declare a local variable live across a barrier whose name matches a struct member (#481) or a lambda
/// parameter (#504), or that declare user labels with the same name in sibling blocks.
/// </summary>
public static partial class HoistedNameIdentityKernels
{
    [GpuKernel]
    public static void BarrierLocalSameNameInitializerKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int X = tid;
        Gpu.SyncThreads();
        var p = new ReproPt { X = 1, Y = 2 };
        output[tid] = X * 100 + p.X;
    }

    [GpuKernel]
    public static void BarrierLocalSameNameWithKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int X = tid;
        Gpu.SyncThreads();
        var p = new ReproPt { X = 1, Y = 2 };
        var q = p with { X = 3 };
        output[tid] = X * 100 + q.X;
    }

    [GpuKernel]
    public static void BarrierLocalSameNameMemberAccessKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        int X = tid;
        Gpu.SyncThreads();
        ReproPt p = default;
        p.Y = 5;
        output[tid] = X * 100 + p.X + p.Y;
    }

    [GpuKernel]
    public static void BarrierLocalSameNameLambdaParameterKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int i = t;
        Gpu.SyncThreads();
        int sum = 0;
        Gpu.ForUnroll(0, 3, i => { sum += i; });
        output[t] = sum + i;
    }

    [GpuKernel]
    public static void SiblingBlockSameLabelKernel(int[] output)
    {
        int t = ThreadIndex.X;
        int acc = t;
        {
            int step = 10;
            Gpu.SyncThreads();
            if ((t & 1) == 0)
            {
                acc += step;
                goto done;
            }

            acc += 100;
            done: ;
        }

        {
            int step = 1000;
            Gpu.SyncThreads();
            if ((t & 1) != 0)
            {
                acc += step;
                goto done;
            }

            acc += 2000;
            done: ;
        }

        output[t] = acc;
    }
}
