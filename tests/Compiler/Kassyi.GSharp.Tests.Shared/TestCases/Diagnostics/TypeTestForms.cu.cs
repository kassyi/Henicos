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

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void ArrayAndNullableTestKernel(int[] data, int[] other, int? maybe)
    {
        data[0] = data is int[] ? 1 : 0;
        data[1] = maybe is int ? 1 : 0;
        data[2] = maybe is not null ? 1 : 0;
        data[3] = other is { } ? 1 : 0;
        data[4] = other is not int[] ? 1 : 0;
        data[5] = (other as int[]) is null ? 1 : 0;
        data[6] = maybe is long ? 1 : 0;
    }

    [GpuKernel]
    [SkipSnapshotConsistency("Diagnostics case")]
    public static void DeclarationPatternKernel(int[] data, TypeTestPoint point, int[] array)
    {
        data[0] = point is ITypeTestShape shape ? 1 : 0;
        data[1] = array is int[] same ? same.Length : 0;
    }
}
