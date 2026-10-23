using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public static partial class NewListKernels
{
    [GpuKernel]
    public static void NewListKernel(int[] result)
    {
        var list = new List<int>();
        result[ThreadIndex.X] = list.Count;
    }
}
