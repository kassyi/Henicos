#if GSHARP_CPU
using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using Kassyi.GSharp.Runtime.Aot;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>Runs a kernel launch on the managed fallback (<see cref="CpuKernelLoader.ForceManagedExecution"/>).</summary>
internal static class ManagedKernelRun
{
    /// <summary>
    /// Runs <paramref name="launch"/> on a thread of its own with managed execution forced, and rethrows its exception.
    /// </summary>
    public static void Run(Action launch)
    {
        var failures = new ConcurrentQueue<Exception>();
        var thread = new Thread(() =>
        {
            try
            {
                CpuKernelLoader.ForceManagedExecution = true;
                launch();
            }
            catch (Exception ex)
            {
                failures.Enqueue(ex);
            }
            finally
            {
                CpuKernelLoader.ForceManagedExecution = false;
            }
        });
        thread.Start();
        thread.Join();

        if (failures.TryDequeue(out var failure))
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
#endif
