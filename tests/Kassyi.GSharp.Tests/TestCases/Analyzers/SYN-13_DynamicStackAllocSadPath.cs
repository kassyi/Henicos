using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public static class SYN13_DynamicStackAllocSadPathCase
{
    [GpuKernel]
    public static void DynamicStackAllocKernel(int dynamicSize, int[] result)
    {
        // Sad Path: stackalloc with a dynamic variable size is not supported in CUDA C++ stack arrays
        Span<int> buffer = stackalloc int[dynamicSize];
        result[0] = buffer[0];
    }
}
