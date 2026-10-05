using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision BigInteger arithmetic via GSharp AOT/JIT transpiled GPU kernels.
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> Acts as a unified facade for GSharp GPU arithmetic, delegating specialized operations to dedicated sub-handlers
/// (<see cref="AotMemoryHandler"/>, <see cref="AotBasicMathHandler"/>, <see cref="AotAdvancedMathHandler"/>, <see cref="AotGraphHandler"/>)
/// while automatically falling back to <see cref="CSharpCpuBigIntProvider"/> when running in CPU fallback or non-GPU testing modes.
/// </para>
/// <para>
/// <b>Zero-Allocation:</b> Relies on pooled memory and kernel launches with zero managed heap allocations in the critical execution loop.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Thread-safe across concurrent stream executions and kernel dispatches.
/// </para>
/// </remarks>
public sealed class GSharpAotProvider : IGpuExecutionProvider
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="GSharpAotProvider"/>.
    /// </summary>
    public static readonly GSharpAotProvider Instance = new();

    private readonly AotMemoryHandler _memory = new();
    private readonly AotBasicMathHandler _basicMath = new();
    private readonly AotAdvancedMathHandler _advancedMath = new();
    private readonly AotGraphHandler _graph = new();

    internal AotAdvancedMathHandler AotAdvancedMath => _advancedMath;

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
    public void FreeMemory(IntPtr devicePointer) => MemoryProvider.FreeMemory(devicePointer);

    /// <inheritdoc />
    public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream) =>
        MemoryProvider.ClearMemoryAsync(devicePointer, numWords, stream);

    /// <inheritdoc />
    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0) =>
        MemoryProvider.CopyHostToDevice(devicePointer, hostData, numWords, stream);

    /// <inheritdoc />
    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0) =>
        MemoryProvider.CopyDeviceToHost(hostData, devicePointer, numWords, stream);

    /// <inheritdoc />
    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0) =>
        MemoryProvider.CopyDeviceToDevice(destPointer, srcPointer, numWords, stream);

    /// <inheritdoc />
    public uint ReadWord(IntPtr devicePointer, int wordOffset = 0) =>
        MemoryProvider.ReadWord(devicePointer, wordOffset);

    /// <inheritdoc />
    public void WriteWord(IntPtr devicePointer, int wordOffset, uint value) =>
        MemoryProvider.WriteWord(devicePointer, wordOffset, value);

    /// <inheritdoc />
    public void SynchronizeStream(IntPtr stream) =>
        MemoryProvider.SynchronizeStream(stream);

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
    public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand) =>
        BasicMathProvider.CompareTo(workspace, leftOperand, rightOperand);

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
    public void LaunchGraph(IntPtr graphExec, IntPtr stream) => GraphProvider.LaunchGraph(graphExec, stream);

    /// <inheritdoc />
    public void DestroyGraph(IntPtr graph, IntPtr graphExec) => GraphProvider.DestroyGraph(graph, graphExec);

    #endregion
}
