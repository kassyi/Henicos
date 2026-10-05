namespace Kassyi.GSharp.Stdlib.GpuMath.Contexts;

/// <summary>
/// Encapsulates device scratch buffers and intermediate memory arrays for multi-precision calculations.
/// </summary>
public struct BufferContext
{
    /// <summary>Primary scratch buffer 1.</summary>
    public uint[] Buffer1;
    /// <summary>Primary scratch buffer 2.</summary>
    public uint[] Buffer2;
    /// <summary>Primary scratch buffer 3.</summary>
    public uint[] Buffer3;
    /// <summary>Temporary intermediate value buffer.</summary>
    public uint[] ValueT;
    /// <summary>Quotient output buffer.</summary>
    public uint[] Quotient;
    /// <summary>Remainder output buffer.</summary>
    public uint[] Remainder;
    /// <summary>Remainder bitmask buffer.</summary>
    public uint[] RemainderMask;
    /// <summary>General-purpose scratch buffer.</summary>
    public uint[] TemporaryBuffer;
    /// <summary>Carry and borrow propagation accumulation buffer.</summary>
    public long[] CarryBuffer;
}

/// <summary>
/// Encapsulates precomputed forward and inverse NTT twiddle factors for multi-prime moduli.
/// </summary>
public struct TwiddleFactorContext
{
    /// <summary>Forward NTT twiddle factors for prime 1.</summary>
    [GpuReadOnly] public uint[] Forward1;
    /// <summary>Forward NTT twiddle factors for prime 2.</summary>
    [GpuReadOnly] public uint[] Forward2;
    /// <summary>Forward NTT twiddle factors for prime 3.</summary>
    [GpuReadOnly] public uint[] Forward3;
    /// <summary>Inverse NTT twiddle factors for prime 1.</summary>
    [GpuReadOnly] public uint[] Inverse1;
    /// <summary>Inverse NTT twiddle factors for prime 2.</summary>
    [GpuReadOnly] public uint[] Inverse2;
    /// <summary>Inverse NTT twiddle factors for prime 3.</summary>
    [GpuReadOnly] public uint[] Inverse3;
}

/// <summary>
/// Encapsulates multi-precision word sizes, bit widths, and iteration counts for big integer algorithms.
/// </summary>
public struct DimensionContext
{
    /// <summary>The base number of 32-bit words in the operands.</summary>
    public int NumberOfWords;
    /// <summary>The extended word count used during intermediate multiplication.</summary>
    public int NumberOfWordsExtended;
    /// <summary>The exponent value in power/modular operations.</summary>
    public int Exponent;
    /// <summary>The bit offset for $2^{2^n} + 1$ Fermat reductions.</summary>
    public int BitOffset2E1;
    /// <summary>The margin word size for precision safety.</summary>
    public int MarginWords;
    /// <summary>The actual bit length of the values.</summary>
    public int ActualBits;
    /// <summary>The margin bits added to prevent precision loss.</summary>
    public int MarginBits;
    /// <summary>The total number of iterations to perform.</summary>
    public int Iterations;
    /// <summary>The target precision in bits.</summary>
    public int TargetBits;
    /// <summary>The total number of bits.</summary>
    public int NumBits;
}

/// <summary>
/// Encapsulates input operands, constant parameters, and destination buffers for arithmetic kernels.
/// </summary>
public struct OperandContext
{
    /// <summary>Left input operand buffer.</summary>
    [GpuReadOnly] public uint[] LeftOperand;
    /// <summary>Right input operand buffer.</summary>
    [GpuReadOnly] public uint[] RightOperand;
    /// <summary>Target operand buffer for in-place or unary transformations.</summary>
    public uint[] TargetOperand;
    /// <summary>Destination output result buffer.</summary>
    public uint[] ResultBuffer;
    /// <summary>General constant operand buffer.</summary>
    public uint[] Constant;
    /// <summary>Specialized constant 3 operand buffer for Newton-Raphson iterations.</summary>
    public uint[] Constant3;
    /// <summary>Carry accumulation buffer for multi-precision addition and subtraction.</summary>
    public long[] CarryBuffer;
}

/// <summary>
/// Encapsulates the complete workspace scratch memory state for Newton-Raphson mega kernels.
/// </summary>
public struct WorkspaceStateContext
{
    /// <summary>Scratch buffer for squared variable $X^2$.</summary>
    public uint[] XSquared;
    /// <summary>Scratch buffer for scaled squared variable $A \times X^2$.</summary>
    public uint[] AxSquared;
    /// <summary>Scratch buffer for current iteration product.</summary>
    public uint[] CurrentProduct;
    /// <summary>Scratch buffer for difference vector.</summary>
    public uint[] Diff;
    /// <summary>Scratch buffer for unshifted next iteration result.</summary>
    public uint[] NextXUnshifted;
    /// <summary>Scratch buffer for bit-shifted next iteration result.</summary>
    public uint[] NextXShifted;
    /// <summary>Scratch buffer for next iteration value.</summary>
    public uint[] NextIter;
    /// <summary>First NTT scratch buffer for operand A.</summary>
    public uint[] NttBuf1A;
    /// <summary>Second NTT scratch buffer for operand A.</summary>
    public uint[] NttBuf2A;
    /// <summary>First NTT scratch buffer for operand B.</summary>
    public uint[] NttBuf1B;
    /// <summary>Second NTT scratch buffer for operand B.</summary>
    public uint[] NttBuf2B;
    /// <summary>General temporary buffer.</summary>
    public uint[] TmpBuf;
    /// <summary>Uncarried lower-half word accumulator.</summary>
    public uint[] UncarriedLow;
    /// <summary>Uncarried upper-half word accumulator.</summary>
    public uint[] UncarriedHigh;
    /// <summary>Block-level carry propagation array.</summary>
    public uint[] BlockCarries;
    public uint[] ModulusExpanded;
}

/// <summary>
/// Context parameters for NTT-related operations in Fermat sequences.
/// </summary>
public struct Ntt
{
    /// <summary>The power-of-two transformation length of the transform.</summary>
    public int TransformLength;
    /// <summary>The scratch buffer context for intermediate convolutions.</summary>
    public BufferContext Buffers;
    /// <summary>The precomputed roots of unity and twiddle factor context.</summary>
    public TwiddleFactorContext TwiddleFactors;
}

/// <summary>
/// Context parameters for the Fermat sequence computation.
/// </summary>
public struct Fermat
{
    /// <summary>The scratch buffer context for intermediate evaluations.</summary>
    public BufferContext Buffers;
    /// <summary>The dimension metadata describing word counts and exponents.</summary>
    public DimensionContext Dimensions;
    /// <summary>The NTT configuration and twiddle factor context.</summary>
    public Ntt Ntt;
}

/// <summary>
/// Context parameters for basic BigInt GPU kernels and MacroTranspiler expansions.
/// </summary>
public struct GpuBigIntContext
{
    /// <summary>The operand buffers containing input values and destination arrays.</summary>
    public OperandContext Operands;
    /// <summary>The total number of 32-bit words in the multi-precision integer.</summary>
    public int NumberOfWords;
}

/// <summary>
/// Context parameters for dual-pass NTT GPU execution.
/// </summary>
public struct DualNtt
{
    /// <summary>First NTT transformation buffer.</summary>
    public uint[] Buffer1;
    /// <summary>Second NTT transformation buffer.</summary>
    public uint[] Buffer2;
    /// <summary>General temporary scratch buffer.</summary>
    public uint[] TemporaryBuffer;
    /// <summary>Twiddle factor table 1.</summary>
    [GpuReadOnly] public uint[] Twiddle1;
    /// <summary>Twiddle factor table 2.</summary>
    [GpuReadOnly] public uint[] Twiddle2;
    /// <summary>Batch dimension parameter M.</summary>
    public int M;
    /// <summary>First transform length dimension N1.</summary>
    public int N1;
    /// <summary>Second transform length dimension N2.</summary>
    public int N2;
}

/// <summary>
/// Execution arguments for NTT-related kernels.
/// </summary>
public struct NumberTheoreticTransform
{
    /// <summary>In-place transform buffer.</summary>
    public uint[] Buffer;
    /// <summary>Temporary scratch buffer.</summary>
    public uint[] TemporaryBuffer;

    /// <summary>Twiddle factor table.</summary>
    [GpuReadOnly]
    public uint[] Twiddle;

    /// <summary>Batch dimension parameter M.</summary>
    public int M;
    /// <summary>First transform length dimension N1.</summary>
    public int N1;
    /// <summary>Second transform length dimension N2.</summary>
    public int N2;
    /// <summary>Prime modulus P.</summary>
    public uint P;
    /// <summary>Barrett reduction or multiplicative inverse parameter Pm.</summary>
    public ulong Pm;
    /// <summary>Indicates whether an inverse NTT is performed.</summary>
    public bool IsInverse;
    /// <summary>Indicates whether twiddle multiplication is applied before transform.</summary>
    public bool ApplyTwiddleBefore;
    /// <summary>Indicates whether twiddle multiplication is applied after transform.</summary>
    public bool ApplyTwiddleAfter;
    /// <summary>The maximum number of GPU blocks to dispatch.</summary>
    public int MaxBlocks;
    /// <summary>The current transform stage length L.</summary>
    public int CurrentL;
}

/// <summary>
/// Context parameters for the Newton-Raphson Sqrt MegaKernel.
/// </summary>
public struct SqrtNewtonRaphson
{
    /// <summary>Input and output operand buffers.</summary>
    public OperandContext Operands;
    /// <summary>Dimension metadata for precision and word sizes.</summary>
    public DimensionContext Dimensions;
    /// <summary>NTT transformation context.</summary>
    public Ntt Ntt;
    /// <summary>State scratch buffers for intermediate iterations.</summary>
    public WorkspaceStateContext Workspace;
    /// <summary>Carry accumulation buffer.</summary>
    public long[] CarryBuffer;
}

/// <summary>
/// Context parameters for the Newton-Raphson Reciprocal (DivRem) MegaKernel.
/// </summary>
public struct ReciprocalNewtonRaphson
{
    /// <summary>Input and output operand buffers.</summary>
    public OperandContext Operands;
    /// <summary>Dimension metadata for precision and word sizes.</summary>
    public DimensionContext Dimensions;
    /// <summary>NTT transformation context.</summary>
    public Ntt Ntt;
    /// <summary>State scratch buffers for intermediate iterations.</summary>
    public WorkspaceStateContext Workspace;
    /// <summary>Carry accumulation buffer.</summary>
    public long[] CarryBuffer;
}

/// <summary>
/// Encapsulates static constant parameters for RNS base extension and modular reductions.
/// </summary>
public struct ModPowNttContext
{
    public OperandContext Operands;
    public uint[] ReciprocalMod;
    public uint[] Exponent;
    public int ExponentWords;
    public DimensionContext Dimensions;
    public Ntt Ntt;
    public WorkspaceStateContext Workspace;
    public long[] CarryBuffer;
}

public struct RnsConstantsContext
{
    [GpuReadOnly] public uint[] ModuliB1;
    [GpuReadOnly] public uint[] ModuliB2;
    public uint ModSk;
    [GpuReadOnly] public uint[] GammaB1;
    [GpuReadOnly] public uint[] GammaB2;
    [GpuReadOnly] public uint[] M1OverMiModMsk;
    public uint M1InvModMsk;
    [GpuReadOnly] public uint[] M2OverMjModMsk;
    public uint M2InvModMsk;
    [GpuReadOnly] public uint[] M1OverMiModMj;
    [GpuReadOnly] public uint[] M2OverMjModMi;
    [GpuReadOnly] public uint[] M1ModMj;
    [GpuReadOnly] public uint[] M2ModMi;
    public int KSize;
}

/// <summary>
/// Represents a multi-precision integer decomposed into Residue Number System spaces.
/// </summary>
public struct RnsBigInt
{
    public uint[] B1;
    public uint[] B2;
    public uint Sk;
}

/// <summary>
/// Encapsulates dynamic constants dependent on the modulus N for Montgomery reduction.
/// </summary>
public struct RnsMontgomeryContext
{
    [GpuReadOnly] public uint[] NRnsB2;
    public uint NSk;
    [GpuReadOnly] public uint[] NegNInvB1;
    [GpuReadOnly] public uint[] RInvB2;
}
