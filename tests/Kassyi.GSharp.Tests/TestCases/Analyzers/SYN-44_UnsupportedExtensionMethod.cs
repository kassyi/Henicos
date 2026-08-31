using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class ListExtensions
{
    public static void ClearList(this List<int> list)
    {
        list.Clear();
    }
}

public static class SYN_44_UnsupportedExtensionMethod
{
    [GpuKernel]
    public static void Kernel(int[] output)
    {
        List<int> list = null!;
        list.ClearList();
    }
}
