using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class SampleRefClass
{
    public int Value;
}

public static class SYN08_DelegateEscapeSadPathCase
{
    public static Func<int, int>? EscapedDelegate;

    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void DelegateEscapeKernel(int[] data)
    {
        // Sad Path: Escape delegate via Func/Action outside method or allocation
        Func<int, int> f = x => x + 1;
        EscapedDelegate = f; // Escapes the kernel boundary
        data[0] = f(data[0]);
    }

    [GpuKernel]
    [Kassyi.GSharp.Core.SkipSnapshotConsistency("Sad Path")]
    public static void ReferenceTypeCaptureKernel(int[] data)
    {
        SampleRefClass obj = new SampleRefClass { Value = 42 };
        int LocalFunc()
        {
            return obj.Value; // Capturing reference type requiring heap allocation / object creation
        }
        data[0] = LocalFunc();
    }
}
