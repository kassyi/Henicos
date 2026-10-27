// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Syntax.SyncLookalike;

// Device functions that share the name SyncThreads with the barrier: a call to one is an ordinary call, which every
// dialect must make (#503). SyncSpellingKernels calls them through a namespace qualified Gpu and through using static.
public static class Gpu
{
    [GpuDeviceFunction]
    public static void SyncThreads(int[] output, int tid)
    {
        output[tid] = tid + 20;
    }
}

public static class UserSync
{
    [GpuDeviceFunction]
    public static void SyncThreads(int[] output, int tid)
    {
        output[tid] = tid + 10;
    }
}
