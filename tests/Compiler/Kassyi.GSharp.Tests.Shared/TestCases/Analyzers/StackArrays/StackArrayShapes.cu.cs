using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.StackArrays;

// GSH0003 for the arrays G# cannot keep as a stack array, GSH0001 for an array created outside a declaration
[SkipSnapshotConsistency("Analyzer test case: StackArrayDiagnosticsTests")]
public static partial class StackArrayShapeKernels
{
    [GpuDeviceFunction]
    public static int Sum(int[] values) => values[0];

    // X5: multidimensional
    [GpuKernel]
    public static void MultidimensionalKernel(int[] output)
    {
        int[,] m = new int[2, 3];
        output[0] = m[1, 2];
    }

    // X6: zero length (a C# array of length 0 is not null, which G# cannot tell apart)
    [GpuKernel]
    public static void ZeroLengthKernel(int[] output)
    {
        int[] a = new int[0];
        int[] b = { };
        int[] c = [];
        output[0] = a.Length + b.Length + c.Length;
    }

    // X7: an array created outside a declaration
    [GpuKernel]
    public static void ArgumentKernel(int[] output)
    {
        output[0] = Sum(new int[4]);
    }

    [GpuDeviceFunction]
    public static int Length(Span<int> values) => values.Length;

    // X8: a stackalloc of a size known only at run time, in an argument
    [GpuKernel]
    public static void DynamicArgumentKernel(int[] output, int n)
    {
        output[0] = Length(stackalloc int[n]);
    }
}
