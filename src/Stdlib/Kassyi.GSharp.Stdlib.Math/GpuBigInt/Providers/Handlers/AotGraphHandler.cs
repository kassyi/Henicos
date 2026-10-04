using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Runtime.Interop;

namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt.Providers.Handlers;

public sealed class AotGraphHandler : IGpuGraphProvider
{
    public void BeginCapture(IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        // CU_STREAM_CAPTURE_MODE_THREAD_LOCAL = 1
        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(stream, 1));
    }



    public IntPtr EndCapture(IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamEndCapture(stream, out var graph));
        return graph;
    }

    public IntPtr InstantiateGraph(IntPtr graph)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuGraphInstantiate(out var graphExec, graph, IntPtr.Zero, IntPtr.Zero,
            0));
        return graphExec;
    }

    public void LaunchGraph(IntPtr graphExec, IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuGraphLaunch(graphExec, stream));
    }

    public void DestroyGraph(IntPtr graph, IntPtr graphExec)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        if (graphExec != IntPtr.Zero)
        {
            CudaDriverApi.cuGraphExecDestroy(graphExec);
        }

        if (graph != IntPtr.Zero)
        {
            CudaDriverApi.cuGraphDestroy(graph);
        }
    }

    public static (uint GridSize, uint BlockSize) CalculateGridDim(int elements, uint blockSize = 256)
    {
        var gridSize = (uint)((elements + blockSize - 1) / blockSize);
        if (gridSize == 0)
        {
            gridSize = 1;
        }

        return (gridSize, blockSize);
    }
}
