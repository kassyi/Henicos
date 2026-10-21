// ReSharper disable All
using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax;

public static partial class LocalFunctionParameterKernels
{
    [GpuKernel]
    public static void ReadOnlyLocalFunctionParameterKernel(int[] output, [GpuReadOnly] int[] table, [GpuReadOnly] int scale)
    {
        int tid = ThreadIndex.X;

        int Lookup([GpuReadOnly] int[] values, [GpuReadOnly] int factor, int index)
        {
            return values[index] * factor;
        }

        output[tid] = Lookup(table, scale, tid);
    }
}
