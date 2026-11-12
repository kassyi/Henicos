namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Defines CUDA Graph capture, instantiation, execution, and cleanup contracts for accelerated sequence execution.
/// </summary>
public interface IGpuGraphProvider
{
    /// <summary>
    /// Begins capturing CUDA kernel launches into a CUDA Graph on the specified stream.
    /// </summary>
    /// <param name="stream">The CUDA stream to capture.</param>
    void BeginCapture(IntPtr stream);

    /// <summary>
    /// Ends capture on the specified stream and returns the resulting unmanaged CUDA Graph handle.
    /// </summary>
    /// <param name="stream">The CUDA stream being captured.</param>
    /// <returns>An unmanaged handle to the captured <c>cudaGraph_t</c>.</returns>
    IntPtr EndCapture(IntPtr stream);

    /// <summary>
    /// Instantiates an executable CUDA Graph instance from a captured graph handle.
    /// </summary>
    /// <param name="graph">The captured <c>cudaGraph_t</c> handle.</param>
    /// <returns>An unmanaged handle to the instantiated <c>cudaGraphExec_t</c>.</returns>
    IntPtr InstantiateGraph(IntPtr graph);

    /// <summary>
    /// Launches an instantiated executable CUDA Graph on the specified stream.
    /// </summary>
    /// <param name="graphExec">The instantiated <c>cudaGraphExec_t</c> handle.</param>
    /// <param name="stream">The stream on which the graph should be queued.</param>
    void LaunchGraph(IntPtr graphExec, IntPtr stream);

    /// <summary>
    /// Releases and destroys the unmanaged CUDA Graph and executable graph instances.
    /// </summary>
    /// <param name="graph">The <c>cudaGraph_t</c> handle to destroy.</param>
    /// <param name="graphExec">The <c>cudaGraphExec_t</c> handle to destroy.</param>
    void DestroyGraph(IntPtr graph, IntPtr graphExec);
}
