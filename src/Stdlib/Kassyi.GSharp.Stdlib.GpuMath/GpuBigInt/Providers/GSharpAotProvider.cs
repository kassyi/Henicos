using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.GpuMath.BigInt.Providers;

/// <summary>
/// Execution provider implementing arbitrary-precision BigInteger arithmetic via GSharp JIT transpiled GPU kernels.
/// This class acts as a Facade, delegating specific operations to specialized sub-handlers.
/// </summary>
public sealed class GSharpAotProvider : IGpuExecutionProvider
{
    /// <summary>Gets the singleton instance of the <see cref="GSharpAotProvider"/>.</summary>
    public static readonly GSharpAotProvider Instance = new();

    private readonly AotMemoryHandler _memory = new();
    private readonly AotBasicMathHandler _basicMath = new();
    private readonly AotAdvancedMathHandler _advancedMath = new();
    private readonly AotGraphHandler _graph = new();

    private IGpuMemoryProvider MemoryProvider =>
        GpuKernelLoader.IsCpuFallbackMode ? CSharpCpuBigIntProvider.Instance : _memory;

    private IGpuBasicMathProvider BasicMathProvider =>
        GpuKernelLoader.IsCpuFallbackMode ? CSharpCpuBigIntProvider.Instance : _basicMath;

    private IGpuAdvancedMathProvider AdvancedMathProvider =>
        GpuKernelLoader.IsCpuFallbackMode ? CSharpCpuBigIntProvider.Instance : _advancedMath;

    private IGpuGraphProvider GraphProvider =>
        GpuKernelLoader.IsCpuFallbackMode ? CSharpCpuBigIntProvider.Instance : _graph;

    #region IGpuMemoryProvider

    public IntPtr AllocateMemory(int numWords) => MemoryProvider.AllocateMemory(numWords);
    public void FreeMemory(IntPtr devicePointer) => MemoryProvider.FreeMemory(devicePointer);

    public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream) =>
        MemoryProvider.ClearMemoryAsync(devicePointer, numWords, stream);

    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0) =>
        MemoryProvider.CopyHostToDevice(devicePointer, hostData, numWords, stream);

    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0) =>
        MemoryProvider.CopyDeviceToHost(hostData, devicePointer, numWords, stream);

    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0) =>
        MemoryProvider.CopyDeviceToDevice(destPointer, srcPointer, numWords, stream);

    public uint ReadWord(IntPtr devicePointer, int wordOffset = 0) =>
        MemoryProvider.ReadWord(devicePointer, wordOffset);

    public void WriteWord(IntPtr devicePointer, int wordOffset, uint value) =>
        MemoryProvider.WriteWord(devicePointer, wordOffset, value);

    #endregion

    #region IGpuBasicMathProvider

    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => BasicMathProvider.Add(workspace, leftOperand, rightOperand, resultBuffer);

    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => BasicMathProvider.Sub(workspace, leftOperand, rightOperand, resultBuffer);

    public void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer) => BasicMathProvider.Add(workspace, leftOperand, rightOperand, resultBuffer);

    public void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer) => BasicMathProvider.Sub(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        BasicMathProvider.BitwiseAnd(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        BasicMathProvider.BitwiseOr(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) =>
        BasicMathProvider.BitwiseXor(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer) =>
        BasicMathProvider.BitwiseNot(workspace, leftOperand, resultBuffer);

    public void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer) => BasicMathProvider.ShiftLeft(workspace, leftOperand, shiftBits, resultBuffer);

    public void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits,
        GpuBigUInt resultBuffer) =>
        BasicMathProvider.ShiftRight(workspace, leftOperand, shiftBits, resultBuffer);

    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords) =>
        BasicMathProvider.Square(workspace, devicePointer, logicalWords);

    public bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        BasicMathProvider.IsZero(workspace, leftOperand);

    public bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        BasicMathProvider.IsOne(workspace, leftOperand);

    public int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand) =>
        BasicMathProvider.GetBitLength(workspace, leftOperand);

    public int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand) =>
        BasicMathProvider.CompareTo(workspace, leftOperand, rightOperand);

    #endregion

    #region IGpuAdvancedMathProvider

    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => AdvancedMathProvider.Mul(workspace, leftOperand, rightOperand, resultBuffer);

    public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand,
        GpuBigUInt resultBuffer) => AdvancedMathProvider.Mul(workspace, leftOperand, rightOperand, resultBuffer);

    public void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int actualBits, int iterations) => AdvancedMathProvider.InverseSqrtMega(workspace,
        targetOperand, resultBuffer, marginWords, actualBits, iterations);

    public void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits) =>
        AdvancedMathProvider.ReciprocalMega(workspace, targetOperand, resultBuffer, marginWords, numBits, marginBits,
            iterations, targetBits);

    public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand,
        GpuBigUInt resultBuffer) => AdvancedMathProvider.Gcd(workspace, leftOperand, rightOperand, resultBuffer);

    #endregion

    #region IGpuGraphProvider

    public void BeginCapture(IntPtr stream) => GraphProvider.BeginCapture(stream);
    public IntPtr EndCapture(IntPtr stream) => GraphProvider.EndCapture(stream);
    public IntPtr InstantiateGraph(IntPtr graph) => GraphProvider.InstantiateGraph(graph);
    public void LaunchGraph(IntPtr graphExec, IntPtr stream) => GraphProvider.LaunchGraph(graphExec, stream);
    public void DestroyGraph(IntPtr graph, IntPtr graphExec) => GraphProvider.DestroyGraph(graph, graphExec);

    #endregion
}
