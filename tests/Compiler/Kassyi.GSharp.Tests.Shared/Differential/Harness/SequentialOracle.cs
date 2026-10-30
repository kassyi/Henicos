#nullable enable
using System.Runtime.ExceptionServices;
using System.Threading;
using Kassyi.GSharp.Core;

namespace Kassyi.GSharp.Tests.Shared.Differential;

/// <summary>
/// Runs the C# body of a kernel once per GPU thread, one thread after another, on a host thread of its own. The body is
/// the method the user wrote, not the managed path's rewrite of it, so it is the C# definition of the result.
/// </summary>
/// <remarks>
/// Only for kernels without barriers, warp intrinsics, shared memory, operator macros or StackLinq floating-point sums:
/// <c>Gpu.SyncThreads</c> returns at once while <see cref="ManagedGpuContext.IsActive"/> is set, so a barrier would
/// silently lose its meaning (design D1; the test-convention analyzer HNT0008 of #975 will report it). The host thread is a new one because the context is
/// <see cref="ThreadStaticAttribute"/>: left on an xUnit thread, it would stop other tests from seeing
/// <see cref="GpuOnlyException"/>.
/// </remarks>
public static class SequentialOracle
{
    /// <summary>Runs <paramref name="body"/> for every thread of <paramref name="shape"/> in <paramref name="order"/>.</summary>
    /// <param name="shape">The launch.</param>
    /// <param name="order">The order of the threads.</param>
    /// <param name="body">The kernel call, e.g. <c>() =&gt; Kernels.AddKernel(a, b, r)</c>.</param>
    public static void Run(LaunchShape shape, ThreadOrder order, Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (int id in order.Enumerate(shape))
                {
                    ManagedGpuContext.SetThreadContext(shape.ContextOf(id));
                    body();
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                ManagedGpuContext.ClearThreadContext();
            }
        });
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
