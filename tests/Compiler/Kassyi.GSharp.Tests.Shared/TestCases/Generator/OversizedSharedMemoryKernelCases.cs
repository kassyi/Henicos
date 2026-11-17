#nullable enable
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.TestCases.Generator;

public static partial class OversizedSharedMemoryKernelCases
{
    [GpuKernel(AllowOversizedSharedMemory = true)]
    public static void Marked(int[] data)
    {
        data[ThreadIndex.X] = 1;
    }

    [GpuKernel]
    public static void Unmarked(int[] data)
    {
        data[ThreadIndex.X] = 2;
    }
}
