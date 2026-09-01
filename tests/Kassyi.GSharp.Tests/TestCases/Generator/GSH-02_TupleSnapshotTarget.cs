using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Generator;

public static class GSH_02_TupleSnapshotTarget
{
    [GpuKernel]
    public static void TupleSnapshotKernel(int[] data)
    {
        var tid = ThreadIndex.X;
        var t1 = (1, 2);
        (int a, (int b, int c)) = (data[tid], t1);
        data[tid] = a + b + c;
    }
}
