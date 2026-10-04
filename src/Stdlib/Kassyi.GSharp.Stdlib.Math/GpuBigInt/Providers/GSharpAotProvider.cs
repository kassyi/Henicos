using Kassyi.GSharp.Runtime.Aot;
using Kassyi.GSharp.Stdlib.Math.GpuBigInt.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.Math.GpuBigInt.Providers;

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

    public void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => BasicMathProvider.Add(workspace, leftOperand, rightOperand, resultBuffer);

    public void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => BasicMathProvider.Sub(workspace, leftOperand, rightOperand, resultBuffer);

    public void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer) => BasicMathProvider.Add(workspace, leftOperand, rightOperand, resultBuffer);

    public void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer) => BasicMathProvider.Sub(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseAnd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        BasicMathProvider.BitwiseAnd(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseOr(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        BasicMathProvider.BitwiseOr(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseXor(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) =>
        BasicMathProvider.BitwiseXor(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseNot(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer) =>
        BasicMathProvider.BitwiseNot(workspace, leftOperand, resultBuffer);

    public void ShiftLeft(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits,
        GpuUIntBigInteger resultBuffer) => BasicMathProvider.ShiftLeft(workspace, leftOperand, shiftBits, resultBuffer);

    public void ShiftRight(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits,
        GpuUIntBigInteger resultBuffer) =>
        BasicMathProvider.ShiftRight(workspace, leftOperand, shiftBits, resultBuffer);

    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords) =>
        BasicMathProvider.Square(workspace, devicePointer, logicalWords);

    public bool IsZero(GpuWorkspace workspace, GpuUIntBigInteger leftOperand) =>
        BasicMathProvider.IsZero(workspace, leftOperand);

    public bool IsOne(GpuWorkspace workspace, GpuUIntBigInteger leftOperand) =>
        BasicMathProvider.IsOne(workspace, leftOperand);

    public int GetBitLength(GpuWorkspace workspace, GpuUIntBigInteger leftOperand) =>
        BasicMathProvider.GetBitLength(workspace, leftOperand);

    public int CompareTo(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        BasicMathProvider.CompareTo(workspace, leftOperand, rightOperand);

    #endregion

    #region IGpuAdvancedMathProvider

    public void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => AdvancedMathProvider.Mul(workspace, leftOperand, rightOperand, resultBuffer);

    public void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer) => AdvancedMathProvider.Mul(workspace, leftOperand, rightOperand, resultBuffer);

    public void InverseSqrtMega(GpuWorkspace workspace, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer,
        int marginWords, int actualBits, int iterations) => AdvancedMathProvider.InverseSqrtMega(workspace,
        targetOperand, resultBuffer, marginWords, actualBits, iterations);

    public void ReciprocalMega(GpuWorkspace workspace, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits) =>
        AdvancedMathProvider.ReciprocalMega(workspace, targetOperand, resultBuffer, marginWords, numBits, marginBits,
            iterations, targetBits);

    public void Gcd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => AdvancedMathProvider.Gcd(workspace, leftOperand, rightOperand, resultBuffer);

    #endregion

    #region IGpuGraphProvider

    public void BeginCapture(IntPtr stream) => GraphProvider.BeginCapture(stream);
    public IntPtr EndCapture(IntPtr stream) => GraphProvider.EndCapture(stream);
    public IntPtr InstantiateGraph(IntPtr graph) => GraphProvider.InstantiateGraph(graph);
    public void LaunchGraph(IntPtr graphExec, IntPtr stream) => GraphProvider.LaunchGraph(graphExec, stream);
    public void DestroyGraph(IntPtr graph, IntPtr graphExec) => GraphProvider.DestroyGraph(graph, graphExec);

    #endregion
}
