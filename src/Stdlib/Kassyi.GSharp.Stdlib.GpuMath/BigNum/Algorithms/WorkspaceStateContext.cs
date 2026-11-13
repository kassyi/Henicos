namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

/// <summary>
/// Encapsulates the complete workspace scratch memory state for Newton-Raphson mega kernels.
/// </summary>
public struct WorkspaceStateContext
{
    /// <summary>Intermediate polynomial iteration scratch buffers.</summary>
    public IterationScratchContext Iteration;
    /// <summary>Dual-prime NTT convolution and CRT carry-resolution scratch buffers.</summary>
    public NttConvolutionScratchContext NttScratch;
}