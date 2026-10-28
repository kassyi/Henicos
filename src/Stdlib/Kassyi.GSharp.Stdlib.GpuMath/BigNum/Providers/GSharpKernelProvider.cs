
namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision BigInteger arithmetic via pre-compiled GSharp GPU kernels.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Acts as a unified facade for GSharp GPU arithmetic, delegating specialized operations to dedicated sub-handlers
/// (<see cref="KernelMemoryHandler"/>, <see cref="KernelBasicMathHandler"/>, <see cref="KernelAdvancedMathHandler"/>, <see cref="KernelGraphHandler"/>)
/// while automatically falling back to <see cref="CSharpCpuBigIntProvider"/> when running in CPU fallback or non-GPU testing modes.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Relies on pooled memory and kernel launches with zero managed heap allocations in the critical execution loop.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Thread-safe across concurrent stream executions and kernel dispatches.
/// </para>
/// </remarks>
public sealed class GSharpKernelProvider : IGpuExecutionProvider
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="GSharpKernelProvider"/>.
    /// </summary>
    public static readonly GSharpKernelProvider Instance = new();

    private readonly KernelMemoryHandler _memory = new();
    private readonly KernelBasicMathHandler _basicMath = new();
    private readonly KernelAdvancedMathHandler _advancedMath = new();
    private readonly KernelGraphHandler _graph = new();

    internal KernelAdvancedMathHandler KernelAdvancedMath => _advancedMath;

    private IGpuMemoryProvider MemoryProvider =>
        GpuKernelLoader.IsCpuFallbackMode ? CSharpCpuBigIntProvider.Instance : _memory;

    private IGpuBasicMathProvider BasicMathProvider =>
        GpuKernelLoader.IsCpuFallbackMode ? CSharpCpuBigIntProvider.Instance : _basicMath;

    private IGpuAdvancedMathProvider AdvancedMathProvider =>
        GpuKernelLoader.IsCpuFallbackMode ? CSharpCpuBigIntProvider.Instance : _advancedMath;

    private IGpuGraphProvider GraphProvider =>
        GpuKernelLoader.IsCpuFallbackMode ? CSharpCpuBigIntProvider.Instance : _graph;


    #region IGpuMemoryProvider

    /// <inheritdoc />
    public IntPtr AllocateMemory(int numWords) => MemoryProvider.AllocateMemory(numWords);

    /// <inheritdoc />
    public void FreeMemory(IntPtr devicePointer, IntPtr stream = 0) => MemoryProvider.FreeMemory(devicePointer, stream);

    /// <inheritdoc />
    public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream) =>
        MemoryProvider.ClearMemoryAsync(devicePointer, numWords, stream);

    /// <inheritdoc />
    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0) =>
        MemoryProvider.CopyHostToDevice(devicePointer, hostData, numWords, stream);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">An on-device correction on the stream did not converge since the
    /// previous read (see <see cref="GpuCorrectionStatus"/>).</exception>
    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0)
    {
        // The copy waits for the stream only when it moves data, so only then can it carry the status words.
        var status = numWords > 0 ? GpuCorrectionStatus.BeginRead(stream) : null;
        try
        {
            MemoryProvider.CopyDeviceToHost(hostData, devicePointer, numWords, stream);
        }
        catch
        {
            // The read did not wait, so the next read copies the status words again.
            status?.AbandonRead();
            throw;
        }

        status?.EndRead();
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The returned task completes with it when an on-device correction on
    /// the stream did not converge since the previous read (see <see cref="GpuCorrectionStatus"/>).</exception>
    public ValueTask CopyDeviceToHostAsync(Memory<uint> hostData, IntPtr devicePointer, int numWords, IntPtr stream,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            // Nothing is read, so the status is left for the next read.
            return ValueTask.FromCanceled(cancellationToken);
        }

        var status = numWords > 0 ? GpuCorrectionStatus.BeginRead(stream) : null;
        ValueTask read;
        try
        {
            read = MemoryProvider.CopyDeviceToHostAsync(hostData, devicePointer, numWords, stream, cancellationToken);
        }
        catch
        {
            status?.AbandonRead();
            throw;
        }

        return status is null ? read : status.EndReadAfter(read);
    }

    /// <inheritdoc />
    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0) =>
        MemoryProvider.CopyDeviceToDevice(destPointer, srcPointer, numWords, stream);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">An on-device correction on the stream did not converge since the
    /// previous read (see <see cref="GpuCorrectionStatus"/>).</exception>
    public uint ReadWord(IntPtr devicePointer, int wordOffset = 0, IntPtr stream = 0)
    {
        var status = GpuCorrectionStatus.BeginRead(stream);
        uint word;
        try
        {
            word = MemoryProvider.ReadWord(devicePointer, wordOffset, stream);
        }
        catch
        {
            status?.AbandonRead();
            throw;
        }

        status?.EndRead();
        return word;
    }

    /// <inheritdoc />
    public void WriteWord(IntPtr devicePointer, int wordOffset, uint value, IntPtr stream = 0) =>
        MemoryProvider.WriteWord(devicePointer, wordOffset, value, stream);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">An on-device correction on the stream did not converge since the
    /// previous read (see <see cref="GpuCorrectionStatus"/>).</exception>
    public void SynchronizeStream(IntPtr stream)
    {
        var status = GpuCorrectionStatus.BeginRead(stream);
        try
        {
            MemoryProvider.SynchronizeStream(stream);
        }
        catch
        {
            status?.AbandonRead();
            throw;
        }

        status?.EndRead();
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The returned task completes with it when an on-device correction on
    /// the stream did not converge since the previous read (see <see cref="GpuCorrectionStatus"/>).</exception>
    public ValueTask SynchronizeStreamAsync(IntPtr stream, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            // Nothing is awaited, so the status is left for the next read.
            return ValueTask.FromCanceled(cancellationToken);
        }

        var status = GpuCorrectionStatus.BeginRead(stream);
        ValueTask read;
        try
        {
            read = MemoryProvider.SynchronizeStreamAsync(stream, cancellationToken);
        }
        catch
        {
            status?.AbandonRead();
            throw;
        }

        return status is null ? read : status.EndReadAfter(read);
    }

    #endregion

    #region IGpuBasicMathProvider

    /// <inheritdoc />
    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => BasicMathProvider.Add(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => BasicMathProvider.Sub(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer) => BasicMathProvider.Add(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer) => BasicMathProvider.Sub(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        BasicMathProvider.BitwiseAnd(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        BasicMathProvider.BitwiseOr(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        BasicMathProvider.BitwiseXor(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer) =>
        BasicMathProvider.BitwiseNot(workspace, leftOperand, resultBuffer);

    /// <inheritdoc />
    public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer) => BasicMathProvider.ShiftLeft(workspace, leftOperand, shiftBits, resultBuffer);

    /// <inheritdoc />
    public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer) =>
        BasicMathProvider.ShiftRight(workspace, leftOperand, shiftBits, resultBuffer);

    /// <inheritdoc />
    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords) =>
        BasicMathProvider.Square(workspace, devicePointer, logicalWords);

    /// <inheritdoc />
    public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        BasicMathProvider.IsZero(workspace, leftOperand);

    /// <inheritdoc />
    public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        BasicMathProvider.IsOne(workspace, leftOperand);

    /// <inheritdoc />
    public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        BasicMathProvider.GetBitLength(workspace, leftOperand);

    /// <inheritdoc />
    public void ReadLeadingBits(GpuWorkspace workspace, GpuBigUInt value, out int bitLength, out ulong leadingBits) =>
        BasicMathProvider.ReadLeadingBits(workspace, value, out bitLength, out leadingBits);

    /// <inheritdoc />
    public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand) =>
        BasicMathProvider.CompareTo(workspace, leftOperand, rightOperand);

    /// <inheritdoc />
    public uint DivideByUInt32(GpuWorkspace workspace, GpuBigUInt dividend, uint divisor,
        GpuBigUInt quotientBuffer) =>
        BasicMathProvider.DivideByUInt32(workspace, dividend, divisor, quotientBuffer);

    #endregion

    #region IGpuAdvancedMathProvider

    /// <inheritdoc />
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => AdvancedMathProvider.Mul(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer) => AdvancedMathProvider.Mul(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int actualBits, int iterations) => AdvancedMathProvider.InverseSqrtMega(workspace,
        targetOperand, resultBuffer, marginWords, actualBits, iterations);

    /// <inheritdoc />
    public void SqrtFloorCorrection(GpuWorkspace workspace, GpuBigUInt operand, GpuBigUInt rootSquared,
        GpuBigUInt root, GpuBigUInt slack, GpuBigUInt trial) =>
        AdvancedMathProvider.SqrtFloorCorrection(workspace, operand, rootSquared, root, slack, trial);

    /// <inheritdoc />
    public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits) =>
        AdvancedMathProvider.ReciprocalMega(workspace, targetOperand, resultBuffer, marginWords, numBits, marginBits,
            iterations, targetBits);

    /// <inheritdoc />
    public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => AdvancedMathProvider.Gcd(workspace, leftOperand, rightOperand, resultBuffer);

    /// <inheritdoc />
    public void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer) =>
        AdvancedMathProvider.ModInverse(workspace, value, modulus, resultBuffer);

    #endregion

    #region IGpuGraphProvider

    /// <inheritdoc />
    public void BeginCapture(IntPtr stream) => GraphProvider.BeginCapture(stream);

    /// <inheritdoc />
    public IntPtr EndCapture(IntPtr stream) => GraphProvider.EndCapture(stream);

    /// <inheritdoc />
    public IntPtr InstantiateGraph(IntPtr graph) => GraphProvider.InstantiateGraph(graph);

    /// <inheritdoc />
    public void LaunchGraph(IntPtr graphExec, IntPtr stream)
    {
        GraphProvider.LaunchGraph(graphExec, stream);

        // A graph may contain a correction kernel, so the next read checks the correction status. Marked after the
        // launch, so that a read on another thread cannot consume the request before the kernel is on the stream.
        GpuCorrectionStatus.Find(stream)?.MarkPending();
    }

    /// <inheritdoc />
    public void DestroyGraph(IntPtr graph, IntPtr graphExec) => GraphProvider.DestroyGraph(graph, graphExec);

    #endregion
}
