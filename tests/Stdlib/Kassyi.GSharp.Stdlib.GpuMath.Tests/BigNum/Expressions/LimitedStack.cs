#nullable enable
namespace Kassyi.GSharp.Stdlib.GpuMath.Tests.BigNum.Expressions;

/// <summary>
/// Runs a test body on a thread with a small stack: a walk that recursed once per level of a deep expression ends the
/// process with a stack overflow there (it cannot be caught; the test host dies), long before the default stack of the
/// test host would.
/// </summary>
internal static class LimitedStack
{
    /// <summary>
    /// Runs <paramref name="body"/> with its own workspace on a thread with a stack of <paramref name="stackSize"/>
    /// bytes, under the CPU fallback or in a CUDA context of its own, and rethrows what it threw.
    /// </summary>
    public static void Run(int stackSize, bool cpuFallback, Action<GpuWorkspace> body)
    {
        if (!cpuFallback)
        {
            CudaCapability.EnsureGpuAvailable();
        }

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            GpuKernelLoader.ForceCpuFallback = cpuFallback;
            GpuKernelLoader.CudaContextScope? scope = null;
            try
            {
                if (!cpuFallback)
                {
                    scope = new GpuKernelLoader.CudaContextScope();
                }

                using var workspace = new GpuWorkspace(1 << 16, GpuExecutionEngine.GSharpAot);
                body(workspace);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                scope?.Dispose();
                GpuKernelLoader.ForceCpuFallback = false;
            }
        }, stackSize);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
