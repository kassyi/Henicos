namespace Kassyi.GSharp.Stdlib.Math.Contexts;

/// <summary>
/// Context parameters for NTT-related operations in Fermat sequences.
/// </summary>
public struct NttContext
{
    public int TransformLength;
    public BuffersContext Buffers;
    public TwiddleFactorsContext TwiddleFactors;

    public struct BuffersContext
    {
        public uint[] Buffer1;
        public uint[] Buffer2;
        public uint[] Buffer3;
        public uint[] TemporaryBuffer;
    }

    public struct TwiddleFactorsContext
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
    public BuffersContext Buffers;
    public DimensionsContext Dimensions;
    public NttContext Ntt;

    public struct BuffersContext
    {
        public uint[] ValueT;
        public uint[] Quotient;
        public uint[] Remainder;
        public uint[] RemainderMask;
        public uint[] TemporaryBuffer;
        public long[] CarryBuffer;
    }

    public struct DimensionsContext
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
    public OperandsContext Operands;
    public int NumberOfWords;

    public struct OperandsContext
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
