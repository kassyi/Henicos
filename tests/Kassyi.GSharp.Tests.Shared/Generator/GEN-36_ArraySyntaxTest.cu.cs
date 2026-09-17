using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Generator.Kernels;

public class ArraySyntaxTestKernels
{
    [GpuKernel]
    public static void ImplicitArrayFormsKernel()
    {
        // Valid implicit stackalloc
        Span<int> implicitStackAlloc = stackalloc[] { 1, 2, 3 };

        // Implicit array creation
        int[] implicitArray = new[] { 4, 5, 6 };

        // Collection expression (C# 12)
        Span<int> collectionExpr = [7, 8, 9];



        // Single element
        Span<int> singleStackAlloc = stackalloc[] { 42 };
    }
}
