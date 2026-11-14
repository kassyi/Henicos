// ReSharper disable All
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Core;

/// <summary>
/// Sibling scopes that declare a <c>SharedMemory&lt;T&gt;</c> of one name, one with the type and one with <c>var</c>
/// (#1084). The CPU state machine declares the local that a barrier crosses at the top of the state function, so a
/// local of the same name in a nested scope must not clash with it in the managed C#.
/// </summary>
public static partial class SharedMemorySiblingScopesKernels
{
    // No barrier is inside either scope: a barrier after them crosses neither declaration
    [GpuKernel]
    public static void BarrierAfterScopesKernel(int[] output, int branch)
    {
        int tid = ThreadIndex.X;
        if (branch > 0)
        {
            SharedMemory<int> s = new(8);
            s[tid] = 10 + tid;
            output[tid] = s[tid];
        }
        else
        {
            var s = new SharedMemory<int>(8);
            s[tid] = 20 + tid;
            output[tid] = s[tid];
        }

        Gpu.SyncThreads();
    }

    // The barrier is inside the first scope, after the declaration that it crosses
    [GpuKernel]
    public static void BarrierInsideScopeKernel(int[] output, int branch)
    {
        int tid = ThreadIndex.X;
        if (branch > 0)
        {
            SharedMemory<int> s = new(8);
            s[tid] = 10 + tid;
            Gpu.SyncThreads();
            output[tid] = s[(tid + 1) % 8];
        }
        else
        {
            var s = new SharedMemory<int>(8);
            s[tid] = 20 + tid;
            output[tid] = s[tid];
        }
    }
}
