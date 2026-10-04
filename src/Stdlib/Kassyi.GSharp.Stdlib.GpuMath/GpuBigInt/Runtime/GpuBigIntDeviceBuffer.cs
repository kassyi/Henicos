namespace Kassyi.GSharp.Stdlib.GpuMath.BigInt.Runtime;

using System;
using System.Threading;

internal sealed class GpuBigIntDeviceBuffer(IntPtr pointer, int totalWords) : IDisposable
{
    private int _disposed;

    public IntPtr Pointer { get; } = pointer;
    public int TotalWords { get; } = totalWords;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            // Note: Actual device memory deallocation (cuMemFree) is managed 
            // by the provider/manager that created this buffer.
        }
    }
}
