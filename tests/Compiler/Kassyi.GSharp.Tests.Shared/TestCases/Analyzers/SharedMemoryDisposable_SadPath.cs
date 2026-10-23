using System;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Analyzers;

public struct Handle : IDisposable
{
    public int Value;

    public void Dispose()
    {
    }
}

public struct Plain
{
    public int Value;
}

public static partial class SharedMemoryDisposableKernels
{
    [GpuKernel]
    public static void DisposableElementKernel(int[] result)
    {
        SharedMemory<Handle> handles = new(64);
        SharedMemory<Plain> plain = new(64);
        SharedMemory<int> numbers = new SharedMemory<int>(64);
        result[ThreadIndex.X] = handles[0].Value + plain[0].Value + numbers[0];
    }
}
