namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// An operation that reads a value from the device refuses a CUDA graph capture before the read: a capture cannot
// record a host read (the read would invalidate it with a driver error), and the graph would replay the choice made for
// the values of the capture. Every such operation goes through this check, so that the refusal has one form.
internal static class CaptureChecks
{
    // DivRem reads the bit lengths of its operands (and % the zero test of its remainder)
    public const string DivisionReads = "The size of a quotient or remainder";

    public const string EvaluateWithoutGraph = "Evaluate the expression without a graph.";

    /// <summary>Gets whether the workspace stream is capturing a CUDA graph.</summary>
    public static bool IsCapturing(GpuWorkspace workspace)
    {
        if (workspace.Stream == IntPtr.Zero || GpuKernelLoader.IsCpuFallbackMode)
        {
            return false;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuStreamIsCapturing(workspace.Stream, out var status));
        return status != CUstreamCaptureStatus.None;
    }

    /// <summary>
    /// Throws when the workspace stream is capturing a CUDA graph: <paramref name="what"/> is the value the operation
    /// reads from the device, and <paramref name="advice"/> what to do instead.
    /// </summary>
    /// <exception cref="NotSupportedException">The stream is capturing.</exception>
    public static void ThrowIfCapturing(GpuWorkspace workspace, string what, string advice = EvaluateWithoutGraph)
    {
        if (IsCapturing(workspace))
        {
            throw new NotSupportedException(
                what + " is read from the device, which a CUDA graph capture cannot record. " + advice);
        }
    }
}
