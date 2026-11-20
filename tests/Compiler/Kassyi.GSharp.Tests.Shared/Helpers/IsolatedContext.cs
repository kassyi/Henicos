using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Shared.Helpers;

/// <summary>A CUDA context other than the global one, like the context of an isolated <c>GpuWorkspace</c>.</summary>
internal sealed class IsolatedContext : IDisposable
{
    private IntPtr _handle;

    private IsolatedContext(IntPtr handle) => _handle = handle;

    public IntPtr Handle => _handle;

    public static IsolatedContext Create() =>
        CudaTestEnvironment.InContext(static () =>
        {
            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGet(out var device, 0));
            CudaException.ThrowIfError(CudaDriverApi.cuCtxCreate_v2(out var handle, 0, device));

            // cuCtxCreate also makes the new context current; pop it so that it is current only inside Run.
            CudaException.ThrowIfError(CudaDriverApi.cuCtxPopCurrent_v2(out _));
            return new IsolatedContext(handle);
        });

    public T Run<T>(Func<T> action)
    {
        using var scope = new GpuKernelLoader.CudaContextScope(_handle);
        return action();
    }

    public void Run(Action action)
    {
        using var scope = new GpuKernelLoader.CudaContextScope(_handle);
        action();
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        var handle = _handle;
        _handle = IntPtr.Zero;
        GpuKernelLoader.DestroyContext(handle);
    }
}
