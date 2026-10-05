namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

public interface IGpuMemoryProvider
{
    IntPtr AllocateMemory(int numWords);
    void FreeMemory(IntPtr devicePointer);
    void ClearMemoryAsync(IntPtr devicePointer, int numWords, IntPtr stream);
    void CopyHostToDevice(IntPtr devicePointer, uint[] hostData, int numWords, IntPtr stream = 0);
    void CopyDeviceToHost(uint[] hostData, IntPtr devicePointer, int numWords, IntPtr stream = 0);
    void CopyDeviceToDevice(IntPtr destPointer, IntPtr srcPointer, int numWords, IntPtr stream = 0);
    uint ReadWord(IntPtr devicePointer, int wordOffset = 0);
    void WriteWord(IntPtr devicePointer, int wordOffset, uint value);
}

public interface IGpuBasicMathProvider
{
    void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
    void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
    void Add(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
    void Sub(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
    void BitwiseAnd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
    void BitwiseOr(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
    void BitwiseXor(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
    void BitwiseNot(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
    void ShiftLeft(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
    void ShiftRight(GpuWorkspace workspace, GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
    bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
    bool IsZero(GpuWorkspace workspace, GpuBigUInt leftOperand);
    bool IsOne(GpuWorkspace workspace, GpuBigUInt leftOperand);
    int GetBitLength(GpuWorkspace workspace, GpuBigUInt leftOperand);
    int CompareTo(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand);
}

public interface IGpuAdvancedMathProvider
{
    void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
    void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
    void InverseSqrtMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords, int actualBits, int iterations);
    void ReciprocalMega(GpuWorkspace workspace, GpuBigUInt targetOperand, GpuBigUInt resultBuffer, int marginWords, int numBits, int marginBits, int iterations, int targetBits);
    void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
    void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
}

public interface IGpuGraphProvider
{
    void BeginCapture(IntPtr stream);
    IntPtr EndCapture(IntPtr stream);
    IntPtr InstantiateGraph(IntPtr graph);
    void LaunchGraph(IntPtr graphExec, IntPtr stream);
    void DestroyGraph(IntPtr graph, IntPtr graphExec);
}

/// <summary>
/// Defines the contract for executing GPU-accelerated BigInteger arithmetic algorithms and managing device memory.
/// </summary>
public interface IGpuExecutionProvider :
    IGpuMemoryProvider,
    IGpuBasicMathProvider,
    IGpuAdvancedMathProvider,
    IGpuGraphProvider
{
}
