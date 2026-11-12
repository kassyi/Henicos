using System.Diagnostics;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.TestCases.Analyzers.HostIntrinsic;

// GPU-only members in device code, and host code that does not run them or uses a member that works on the host.
public static partial class GpuOnlyHostReferencesHappyPath
{
    // Lambdas and local functions inside GPU functions: HostIntrinsicNestedHappyPath
    [GpuKernel]
    public static void Kernel(int[] output)
    {
        output[ThreadIndex.X] = Device();
        Gpu.SyncThreads();
    }

    [GpuDeviceFunction]
    public static int Device() => BlockDimension.X;

    public static int Host(int[]? data)
    {
        string name = nameof(ThreadIndex.X) + nameof(Gpu.SyncThreads);
        int[] array = Gpu.NotNull(data);
        Gpu.Hint.Unroll();
        var shared = new SharedMemory<int>(4);
        Log(ThreadIndex.X);
        return name.Length + array.Length;
    }

    // C# removes the call, and its argument with it
    [Conditional("GSHARP_UNDEFINED_SYMBOL")]
    private static void Log(int value)
    {
    }
}
