// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class GSH_0007_ForEachSadPath
{
    // 1. Sad Path: Basic foreach loop inside a [GpuKernel]
    [GpuKernel]
    public void BasicForEachKernel(int[] array)
    {
        foreach (var item in array)
        {
            _ = item;
        }
    }

    // 2. Edge Case: Nested foreach loops
    [GpuKernel]
    public void NestedForEachKernel(int[] outerArray, int[] innerArray)
    {
        foreach (var outer in outerArray)
        {
            foreach (var inner in innerArray)
            {
                _ = outer + inner;
            }
        }
    }

    // 3. Edge Case: foreach over a Span<T>
    [GpuKernel]
    public void ForeachOverSpanKernel(Span<int> span)
    {
        foreach (var val in span)
        {
            _ = val;
        }
    }

    // 4. Edge Case: foreach inside a local function within a [GpuKernel]
    [GpuKernel]
    public void ForeachInLocalFunctionKernel(int[] array)
    {
        void LocalFunc()
        {
            foreach (var item in array)
            {
                _ = item;
            }
        }

        LocalFunc();
    }
}
