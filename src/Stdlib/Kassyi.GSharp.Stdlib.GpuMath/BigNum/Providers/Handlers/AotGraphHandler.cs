
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

/// <summary>
/// Provides CUDA Graph capture, compilation, instantiation, and execution management for GSharp BigNum kernel pipelines.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> CUDA Graph capture eliminates CPU launch latency for repetitive arithmetic expressions and iterative solvers (e.g. Newton-Raphson, Chudnovsky).
/// </para>
/// </remarks>
public sealed class AotGraphHandler : IGpuGraphProvider
{
    /// <inheritdoc/>
    public void BeginCapture(IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        // CU_STREAM_CAPTURE_MODE_THREAD_LOCAL = 1
        CudaException.ThrowIfError(CudaDriverApi.cuStreamBeginCapture(stream, 1));
    }

    /// <inheritdoc/>
    public IntPtr EndCapture(IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuStreamEndCapture(stream, out var graph));
        return graph;
    }

    /// <inheritdoc/>
    public IntPtr InstantiateGraph(IntPtr graph)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuGraphInstantiate(out var graphExec, graph, IntPtr.Zero, IntPtr.Zero,
            0));
        return graphExec;
    }

    /// <inheritdoc/>
    public void LaunchGraph(IntPtr graphExec, IntPtr stream)
    {
        using var scope = new GpuKernelLoader.CudaContextScope();
        CudaException.ThrowIfError(CudaDriverApi.cuGraphLaunch(graphExec, stream));
    }

    /// <inheritdoc/>
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

    /// <summary>
    /// Calculates the optimal grid and block dimensions for 1D kernel execution given the element count.
    /// </summary>
    /// <param name="elements">The total number of work items (limbs/words) to process.</param>
    /// <param name="blockSize">The number of threads per thread block (defaults to 256).</param>
    /// <returns>A tuple containing the computed grid size and block size.</returns>
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

