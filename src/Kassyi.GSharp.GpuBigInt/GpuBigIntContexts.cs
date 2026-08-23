namespace Kassyi.GSharp.GpuBigInt;

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
        public uint[] TemporaryBuffer;
    }

    public struct TwiddleFactorsContext
    {
        public uint[] Forward1;
        public uint[] Forward2;
        public uint[] Inverse1;
        public uint[] Inverse2;
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
/// Context parameters for basic BigInt GPU kernels.
/// </summary>
public struct BigIntKernelContext
{
    public OperandsContext Operands;
    public int NumberOfWords;

    public struct OperandsContext
    {
        public uint[] LeftOperand;
        public uint[] RightOperand;
        public uint[] ResultBuffer;
        public long[] CarryBuffer;
    }
}