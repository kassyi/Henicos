using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Diagnostics;

// is / as type tests and object locals in device code
public interface ITypeTestShape { }

public struct TypeTestPoint : ITypeTestShape { public int X; }

public static partial class TypeTestKernels
{
    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ObjectLocalKernel(int[] data)
    {
        object x = null;
        data[0] = x is int ? 1 : 0;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ValueTypeTestKernel(int[] data, int v, TypeTestPoint point)
    {
        data[0] = v is int ? 1 : 0;
        data[1] = v is long ? 1 : 0;
        data[2] = point is ITypeTestShape ? 1 : 0;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void AsKernel(int[] data, TypeTestPoint point)
    {
        data[0] = (point as ITypeTestShape) == null ? 0 : 1;
    }
}
