// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers;

public class UnsupportedClass
{
    public int Value;
}

public static class GSH_02_TupleWithReferenceTypeSadPath
{
    [GpuKernel]
    public static void TupleWithRefTypeKernel(int[] data)
    {
        int tid = ThreadIndex.X;
        // Banned: Tuple containing reference type inside GpuKernel
        (UnsupportedClass obj, int val) tuple = (new UnsupportedClass(), data[tid]);
        data[tid] = tuple.val;
    }
}
