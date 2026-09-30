using Kassyi.GSharp.Stdlib.Math.BigInt.Providers.Handlers;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

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

    #region IGpuMemoryProvider

    public IntPtr AllocateMemory(int numWords) => _memory.AllocateMemory(numWords);
    public void FreeMemory(IntPtr devicePointer) => _memory.FreeMemory(devicePointer);

    public void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream) =>
        _memory.ClearMemoryAsync(devicePointer, numWords, stream);

    public void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0) =>
        _memory.CopyHostToDevice(devicePointer, hostData, numWords, stream);

    public void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0) =>
        _memory.CopyDeviceToHost(hostData, devicePointer, numWords, stream);

    public void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0) =>
        _memory.CopyDeviceToDevice(destPointer, srcPointer, numWords, stream);

    public uint ReadWord(IntPtr devicePointer, int wordOffset = 0) => _memory.ReadWord(devicePointer, wordOffset);

    public void WriteWord(IntPtr devicePointer, int wordOffset, uint value) =>
        _memory.WriteWord(devicePointer, wordOffset, value);

    #endregion

    #region IGpuBasicMathProvider

    public void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => _basicMath.Add(workspace, leftOperand, rightOperand, resultBuffer);

    public void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => _basicMath.Sub(workspace, leftOperand, rightOperand, resultBuffer);

    public void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer) => _basicMath.Add(workspace, leftOperand, rightOperand, resultBuffer);

    public void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer) => _basicMath.Sub(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseAnd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => _basicMath.BitwiseAnd(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseOr(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => _basicMath.BitwiseOr(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseXor(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => _basicMath.BitwiseXor(workspace, leftOperand, rightOperand, resultBuffer);

    public void BitwiseNot(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer) =>
        _basicMath.BitwiseNot(workspace, leftOperand, resultBuffer);

    public void ShiftLeft(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits,
        GpuUIntBigInteger resultBuffer) => _basicMath.ShiftLeft(workspace, leftOperand, shiftBits, resultBuffer);

    public void ShiftRight(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits,
        GpuUIntBigInteger resultBuffer) => _basicMath.ShiftRight(workspace, leftOperand, shiftBits, resultBuffer);

    public bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords) =>
        _basicMath.Square(workspace, devicePointer, logicalWords);

    public bool IsZero(GpuWorkspace workspace, GpuUIntBigInteger leftOperand) =>
        _basicMath.IsZero(workspace, leftOperand);

    public bool IsOne(GpuWorkspace workspace, GpuUIntBigInteger leftOperand) =>
        _basicMath.IsOne(workspace, leftOperand);

    public int GetBitLength(GpuWorkspace workspace, GpuUIntBigInteger leftOperand) =>
        _basicMath.GetBitLength(workspace, leftOperand);

    public int CompareTo(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand) =>
        _basicMath.CompareTo(workspace, leftOperand, rightOperand);

    #endregion

    #region IGpuAdvancedMathProvider

    public void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => _advancedMath.Mul(workspace, leftOperand, rightOperand, resultBuffer);

    public void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand,
        GpuUIntBigInteger resultBuffer) => _advancedMath.Mul(workspace, leftOperand, rightOperand, resultBuffer);

    public void InverseSqrtMega(GpuWorkspace workspace, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer,
        int marginWords, int actualBits, int iterations) => _advancedMath.InverseSqrtMega(workspace, targetOperand,
        resultBuffer, marginWords, actualBits, iterations);

    public void ReciprocalMega(GpuWorkspace workspace, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer,
        int marginWords, int numBits, int marginBits, int iterations, int targetBits) => _advancedMath.ReciprocalMega(
        workspace, targetOperand, resultBuffer, marginWords, numBits, marginBits, iterations, targetBits);

    public void Gcd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand,
        GpuUIntBigInteger resultBuffer) => _advancedMath.Gcd(workspace, leftOperand, rightOperand, resultBuffer);

    #endregion

    #region IGpuGraphProvider

    public void BeginCapture(IntPtr stream) => _graph.BeginCapture(stream);
    public IntPtr EndCapture(IntPtr stream) => _graph.EndCapture(stream);
    public IntPtr InstantiateGraph(IntPtr graph) => _graph.InstantiateGraph(graph);
    public void LaunchGraph(IntPtr graphExec, IntPtr stream) => _graph.LaunchGraph(graphExec, stream);
    public void DestroyGraph(IntPtr graph, IntPtr graphExec) => _graph.DestroyGraph(graph, graphExec);

    #endregion
}
