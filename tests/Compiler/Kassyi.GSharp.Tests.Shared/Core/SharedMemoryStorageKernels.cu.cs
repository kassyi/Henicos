// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Kernels that declare a <c>SharedMemory&lt;T&gt;</c> storage slot by a pattern variable (<c>x is SharedMemory&lt;T&gt; v</c>)
/// instead of a direct creation.
/// </summary>
public static partial class SharedMemoryStorageKernels
{
    [GpuKernel]
    public static void PatternVariableKernel(int[] output)
    {
        int tid = ThreadIndex.X;
        var a = new SharedMemory<int>(8);
        if (a is SharedMemory<int> v)
        {
            v[tid] = tid + 1;
        }

        output[tid] = a[tid];
    }
}
