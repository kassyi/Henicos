// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

/// <summary>
/// Kernels with one declaration whose declarators are partly moved into the context of the CPU state machine and partly
/// stay locals (#1257). The label <c>Done</c> starts a state, so a declarator used after it lives in the context; the
/// others stay locals. The declarators must still run in the order written. The writes after <c>Done</c> only make the
/// declarators they name live in the context.
/// </summary>
public static partial class MixedHoistDeclarationKernels
{
    [GpuKernel]
    public static void HoistedBeforeLocalKernel(int[] o, int[] p, int rounds)
    {
        int tid = ThreadIndex.X;
        for (int r = 0; r < rounds; r++)
        {
            Gpu.SyncThreads();
            if (r > 100) goto Done;
        }

        int a = tid, b = a + 1;
        p[tid] = b;
    Done:
        a = 7;
        o[tid] = a;
    }

    [GpuKernel]
    public static void SideEffectOrderKernel(int[] o, int[] p, int rounds)
    {
        int tid = ThreadIndex.X;
        int i = tid * 10;
        for (int r = 0; r < rounds; r++)
        {
            Gpu.SyncThreads();
            if (r > 100) goto Done;
        }

        int a = i++, b = i++;
        o[tid] = a;
        p[tid] = b;
    Done:
        a = 5;
    }

    [GpuKernel]
    public static void AlternatingHoistKernel(int[] o, int[] p, int rounds)
    {
        int tid = ThreadIndex.X;
        for (int r = 0; r < rounds; r++)
        {
            Gpu.SyncThreads();
            if (r > 100) goto Done;
        }

        int a = tid, b = a + 1, c = b * 2, d = c + a;
        o[tid] = a * 1000 + c;
        p[tid] = b * 1000 + d;
    Done:
        a = 5;
        c = 6;
    }
}
