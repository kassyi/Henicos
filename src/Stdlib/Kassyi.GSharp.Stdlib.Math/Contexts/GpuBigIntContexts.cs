namespace Kassyi.GSharp.Stdlib.Math.Contexts;

public struct BufferContext
{
    public uint[] Buffer1;
    public uint[] Buffer2;
    public uint[] Buffer3;
    public uint[] ValueT;
    public uint[] Quotient;
    public uint[] Remainder;
    public uint[] RemainderMask;
    public uint[] TemporaryBuffer;
    public long[] CarryBuffer;
}

public struct TwiddleFactorContext
{
    [GpuReadOnly] public uint[] Forward1;
    [GpuReadOnly] public uint[] Forward2;
    [GpuReadOnly] public uint[] Forward3;
    [GpuReadOnly] public uint[] Inverse1;
    [GpuReadOnly] public uint[] Inverse2;
    [GpuReadOnly] public uint[] Inverse3;
}

public struct DimensionContext
{
    public int NumberOfWords;
    public int NumberOfWordsExtended;
    public int Exponent;
    public int BitOffset2E1;
    public int MarginWords;
    public int ActualBits;
    public int MarginBits;
    public int Iterations;
    public int TargetBits;
    public int NumBits;
}

public struct OperandContext
{
    [GpuReadOnly] public uint[] LeftOperand;
    [GpuReadOnly] public uint[] RightOperand;
    public uint[] TargetOperand;
    public uint[] ResultBuffer;
    public uint[] Constant;
    public uint[] Constant3;
    public long[] CarryBuffer;
}

public struct WorkspaceStateContext
{
    public uint[] XSquared;
    public uint[] AxSquared;
    public uint[] CurrentProduct;
    public uint[] Diff;
    public uint[] NextXUnshifted;
    public uint[] NextXShifted;
    public uint[] NextIter;
    public uint[] NttBuf1A;
    public uint[] NttBuf2A;
    public uint[] NttBuf1B;
    public uint[] NttBuf2B;
    public uint[] TmpBuf;
    public uint[] UncarriedLow;
    public uint[] UncarriedHigh;
    public uint[] BlockCarries;
}

/// <summary>
/// Context parameters for NTT-related operations in Fermat sequences.
/// </summary>
public struct Ntt
{
    public int TransformLength;
    public BufferContext Buffers;
    public TwiddleFactorContext TwiddleFactors;
}

/// <summary>
/// Context parameters for the Fermat sequence computation.
/// </summary>
public struct Fermat
{
    public BufferContext Buffers;
    public DimensionContext Dimensions;
    public Ntt Ntt;
}

/// <summary>
/// Context parameters for basic BigInt GPU kernels and MacroTranspiler expansions.
/// </summary>
public struct GpuBigIntContext
{
    public OperandContext Operands;
    public int NumberOfWords;
}

public struct DualNtt
{
    public uint[] Buffer1;
    public uint[] Buffer2;
    public uint[] TemporaryBuffer;
    [GpuReadOnly] public uint[] Twiddle1;
    [GpuReadOnly] public uint[] Twiddle2;
    public int M;
    public int N1;
    public int N2;
}

/// <summary>
/// Execution arguments for NTT-related kernels.
/// </summary>
public struct NumberTheoreticTransform
{
    public uint[] Buffer;
    public uint[] TemporaryBuffer;

    [GpuReadOnly]
    public uint[] Twiddle;

    public int M;
    public int N1;
    public int N2;
    public uint P;
    public ulong Pm;
    public bool IsInverse;
    public bool ApplyTwiddleBefore;
    public bool ApplyTwiddleAfter;
    public int MaxBlocks;
    public int CurrentL;
}

/// <summary>
/// Context parameters for the Newton-Raphson Sqrt MegaKernel.
/// </summary>
public struct SqrtNewtonRaphson
{
    public OperandContext Operands;
    public DimensionContext Dimensions;
    public Ntt Ntt;
    public WorkspaceStateContext Workspace;
    public long[] CarryBuffer;
}

/// <summary>
/// Context parameters for the Newton-Raphson Reciprocal (DivRem) MegaKernel.
/// </summary>
public struct ReciprocalNewtonRaphson
{
    public OperandContext Operands;
    public DimensionContext Dimensions;
    public Ntt Ntt;
    public WorkspaceStateContext Workspace;
    public long[] CarryBuffer;
}
