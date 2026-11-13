#if GSHARP_CPU
// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// A static <c>SharedMemory&lt;T&gt;</c> of more than <c>int.MaxValue</c> bytes, for the managed path (#1280 review).
/// CPU only: CUDA rejects a static declaration of this size in ptxas, and the native path is not launched (it would try
/// to allocate the 4 GB).
/// </summary>
[SkipSnapshotConsistency("CPU only (#if GSHARP_CPU); the snapshot test parses without that symbol")]
public static partial class SharedMemoryHugeStaticKernels
{
    // 2^29 + 16 doubles are 2^32 + 128 bytes, which an int product wraps to 128
    [GpuKernel]
    public static void HugeStaticKernel(double[] output)
    {
#pragma warning disable GSH0013 // The size is the point of the test
        var cache = new SharedMemory<double>(536_870_928);
#pragma warning restore GSH0013
        cache[20] = 5.0;
        output[0] = cache[20];
    }
}
#endif
