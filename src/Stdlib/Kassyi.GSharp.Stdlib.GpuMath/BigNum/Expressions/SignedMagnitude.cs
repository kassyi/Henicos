namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The magnitude operations of the signed expressions whose exact result can need one more word than the operands. The
/// result keeps the width of the operands, and an overflow is reported as <see cref="GpuOverflowException"/> instead of
/// being dropped.
/// </summary>
/// <remarks>
/// On the GPU the check adds no synchronization: the kernel flags the overflow in the workspace's device status words
/// (<see cref="GpuDeviceStatus"/>), and the next read of the workspace stream throws. Without status words (CPU fallback
/// mode, the null stream) the carry is checked on the host right after the operation.
/// </remarks>
internal static class SignedMagnitude
{
    /// <summary>
    /// <c>result = a + b</c> on the width of the operands, reporting an overflow of <paramref name="node"/> when the exact
    /// sum needs another word.
    /// </summary>
    /// <param name="a">The first magnitude.</param>
    /// <param name="b">The second magnitude.</param>
    /// <param name="result">The result; it must not overlap <paramref name="a"/> or <paramref name="b"/>.</param>
    /// <param name="node">The node whose result this is, for the report.</param>
    /// <param name="operation">The operator of <paramref name="node"/>.</param>
    /// <param name="operatorStackTrace">The stack trace of the operator, when captured.</param>
    /// <exception cref="ArgumentException">The three magnitudes do not have the same number of words.</exception>
    /// <exception cref="GpuOverflowException">The sum overflowed (CPU fallback mode only; on the GPU, at the next read).</exception>
    public static void AddChecked(GpuBigUInt a, GpuBigUInt b, GpuBigUInt result, IGpuBigIntExpression node,
        string operation, StackTrace? operatorStackTrace)
    {
        var workspace = a.Workspace;
        if (workspace.DeviceStatus is not { } status || workspace.Provider is not GSharpKernelProvider ||
            GpuKernelLoader.IsCpuFallbackMode)
        {
            GpuBigIntMath.Add(a, b, result);
            ThrowIfWrapped(a, result, node, operation, operatorStackTrace);
            return;
        }

        if (b.NumWords != a.NumWords || result.NumWords != a.NumWords)
        {
            throw new ArgumentException("The operands and the result must have the same number of words.");
        }

        var origin = status.Origins.Register(node, operation, result.NumWords, operatorStackTrace);
        GpuBigIntKernels.LaunchCheckedParallelAddKernelPtr(
            a.DevicePointer, b.DevicePointer, result.DevicePointer, a.NumWords, status.DevicePointer, origin,
            gridDimX: 1, blockDimX: KernelBasicMathHandler.SingleBlockSize(a.NumWords), hStream: workspace.Stream);

        // Marked after the launch, so that a read on another thread cannot consume the request before the kernel.
        status.MarkPending();
    }

    // The host check: a sum taken modulo 2^(32 n) wrapped exactly when it is smaller than an operand
    private static void ThrowIfWrapped(GpuBigUInt operand, GpuBigUInt result, IGpuBigIntExpression node,
        string operation, StackTrace? operatorStackTrace)
    {
        if (result.CompareTo(operand) < 0)
        {
            throw GpuOverflowReport.CreateImmediate(node, operation, result.NumWords, operatorStackTrace);
        }
    }
}
