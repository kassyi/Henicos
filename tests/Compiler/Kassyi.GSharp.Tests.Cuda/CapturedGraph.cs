using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Tests.Cuda;

/// <summary>
/// The launches that a test captured into a CUDA graph, and the graph's executable instance; both are destroyed on
/// <see cref="Dispose"/>.
/// </summary>
internal sealed class CapturedGraph(IntPtr graph, IntPtr exec) : IDisposable
{
    /// <summary>
    /// Captures what <paramref name="launch"/> enqueues on <paramref name="stream"/> (the stream handle is its
    /// argument) and instantiates it.
    /// </summary>
    public static CapturedGraph Capture(CudaStream stream, Action<IntPtr> launch)
    {
        var begin = CudaDriverApi.cuStreamBeginCapture(stream.Handle, CUstreamCaptureMode.ThreadLocal);
        CudaException.ThrowIfError(begin);
        IntPtr graph;
        try
        {
            launch(stream.Handle);
        }
        finally
        {
            CudaException.ThrowIfError(CudaDriverApi.cuStreamEndCapture(stream.Handle, out graph));
        }

        var result = CudaDriverApi.cuGraphInstantiate(out var exec, graph, IntPtr.Zero, IntPtr.Zero, 0);
        if (result != CUresult.CudaSuccess)
        {
            CudaDriverApi.cuGraphDestroy(graph);
            CudaException.ThrowIfError(result);
        }

        return new CapturedGraph(graph, exec);
    }

    /// <summary>Launches the graph on <paramref name="stream"/>.</summary>
    public void Launch(CudaStream stream) =>
        CudaException.ThrowIfError(CudaDriverApi.cuGraphLaunch(exec, stream.Handle));

    public void Dispose()
    {
        CudaDriverApi.cuGraphExecDestroy(exec);
        CudaDriverApi.cuGraphDestroy(graph);
    }
}
