namespace Kassyi.GSharp.Stdlib.Math.Contexts;

/// <summary>
/// Context parameters for NTT-related operations in Fermat sequences.
/// </summary>
public struct NttContext
{
    public int TransformLength;
    public BufferContext Buffers;
    public TwiddleFactorContext TwiddleFactors;

    public struct BufferContext
    {
        public uint[] Buffer1;
        public uint[] Buffer2;
        public uint[] Buffer3;
        public uint[] TemporaryBuffer;
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
}

/// <summary>
/// Context parameters for the Fermat sequence computation.
/// </summary>
public struct FermatContext
{
    public BufferContext Buffers;
    public DimensionContext Dimensions;
    public NttContext Ntt;

    public struct BufferContext
    {
        public uint[] ValueT;
        public uint[] Quotient;
        public uint[] Remainder;
        public uint[] RemainderMask;
        public uint[] TemporaryBuffer;
        public long[] CarryBuffer;
    }

    public struct DimensionContext
    {
        public int NumberOfWords;
        public int NumberOfWordsExtended;
        public int Exponent;
        public int BitOffset2E1;
    }
}

/// <summary>
/// Context parameters for basic BigInt GPU kernels and MacroTranspiler expansions.
/// </summary>
public struct BigIntKernelContext
{
    public OperandContext Operands;
    public int NumberOfWords;

    public struct OperandContext
    {
        [GpuReadOnly] public uint[] LeftOperand;
        [GpuReadOnly] public uint[] RightOperand;
        public uint[] ResultBuffer;
        public long[] CarryBuffer;
    }
}

/// <summary>
/// Execution arguments for NTT-related kernels.
/// </summary>
public struct NumberTheoreticTransformKernelArgs
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
public struct SqrtNewtonRaphsonContext
{
    public OperandContext Operands;
    public DimensionContext Dimensions;
    public NttContext Ntt;
    public WorkspaceStateContext Workspace;
    public long[] CarryBuffer;

    public struct OperandContext
    {
        public uint[] TargetOperand;
        public uint[] ResultBuffer;
        public uint[] Constant3;
    }

    public struct DimensionContext
    {
        public int NumberOfWords;
        public int MarginWords;
        public int ActualBits;
        public int MarginBits;
        public int Iterations;
    }

    public struct WorkspaceStateContext
    {
        public uint[] XSquared;
        public uint[] AxSquared;
        public uint[] Diff;
        public uint[] NextXUnshifted;
        public uint[] NextXShifted;

        public uint[] NttBuf1A;
        public uint[] NttBuf2A;
        public uint[] NttBuf1B;
        public uint[] NttBuf2B;
        public uint[] TmpBuf;
        public uint[] UncarriedLow;
        public uint[] UncarriedHigh;
        public uint[] BlockCarries;
    }
}

/// <summary>
/// Context parameters for the Newton-Raphson Reciprocal (DivRem) MegaKernel.
/// </summary>
public struct ReciprocalNewtonRaphsonContext
{
    public OperandContext Operands;
    public DimensionContext Dimensions;
    public NttContext Ntt;
    public WorkspaceStateContext Workspace;
    public long[] CarryBuffer;

    public struct OperandContext
    {
        public uint[] TargetOperand;
        public uint[] ResultBuffer;
        public uint[] Constant;
    }

    public struct DimensionContext
    {
        public int NumberOfWords;
        public int MarginWords;
        public int Iterations;
        public int TargetBits;
        public int NumBits;
        public int MarginBits;
    }

    public struct WorkspaceStateContext
    {
        public uint[] CurrentProduct;
        public uint[] Diff;
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
}
