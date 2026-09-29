using System;

namespace Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

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
    void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
    void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
    void Add(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
    void Sub(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
    void BitwiseAnd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
    void BitwiseOr(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
    void BitwiseXor(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
    void BitwiseNot(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer);
    void ShiftLeft(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);
    void ShiftRight(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);
    bool Square(GpuWorkspace workspace, IntPtr devicePointer, int logicalWords);
    bool IsZero(GpuWorkspace workspace, GpuUIntBigInteger leftOperand);
    bool IsOne(GpuWorkspace workspace, GpuUIntBigInteger leftOperand);
    int GetBitLength(GpuWorkspace workspace, GpuUIntBigInteger leftOperand);
    int CompareTo(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand);
}

public interface IGpuAdvancedMathProvider
{
    void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
    void Mul(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
    void InverseSqrtMega(GpuWorkspace workspace, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords, int actualBits, int iterations);
    void ReciprocalMega(GpuWorkspace workspace, GpuUIntBigInteger targetOperand, GpuUIntBigInteger resultBuffer, int marginWords, int numBits, int marginBits, int iterations, int targetBits);
    void Gcd(GpuWorkspace workspace, GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
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
