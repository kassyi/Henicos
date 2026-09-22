using System.Collections.Generic;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class ManualEnumeratorSadPath
{
    [GpuKernel]
    public static void KernelMethod()
    {
        var list = new List<int>();
        var enumerator = list.GetEnumerator();
    }
}
