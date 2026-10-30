namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

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
    /// <summary>The bit offset <c>2E + 1</c> of a fold modulo <c>2^(2E+1) - 2^E + 11</c> (no kernel reads it at present).</summary>
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
/// Encapsulates intermediate multi-precision polynomial and iteration scratch buffers for Newton-Raphson and Barrett steps.
/// </summary>
public struct IterationScratchContext
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
    /// <summary>Expanded modulus for Barrett reduction.</summary>
    public uint[] ModulusExpanded;
}

/// <summary>
/// Encapsulates dual-prime NTT convolution and CRT carry-resolution scratch buffers.
/// </summary>
public struct NttConvolutionScratchContext
{
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
}

/// <summary>
/// Encapsulates the complete workspace scratch memory state for Newton-Raphson mega kernels.
/// </summary>
public struct WorkspaceStateContext
{
    /// <summary>Intermediate polynomial iteration scratch buffers.</summary>
    public IterationScratchContext Iteration;
    /// <summary>Dual-prime NTT convolution and CRT carry-resolution scratch buffers.</summary>
    public NttConvolutionScratchContext NttScratch;
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
/// Encapsulates the 2D Six-Step NTT grid dimensions ($M = N_1 \times N_2$).
/// </summary>
public struct NttGridContext
{
    /// <summary>Batch/total transform dimension parameter M.</summary>
    public int M;
    /// <summary>First transform length dimension N1.</summary>
    public int N1;
    /// <summary>Second transform length dimension N2.</summary>
    public int N2;
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
    /// <summary>2D NTT grid dimensions ($M, N_1, N_2$).</summary>
    public NttGridContext Grid;
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

    /// <summary>2D NTT grid dimensions ($M, N_1, N_2$).</summary>
    public NttGridContext Grid;
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
/// Common execution state for iterative Newton-Raphson and NTT-based MegaKernels.
/// </summary>
public struct NewtonRaphsonContext
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
    /// <summary>
    /// The workspace's correction status words: the final correction adds 1 to the failure count in word 0 and writes
    /// its code to word 1 when it does not converge, and the host reports a new count at the next read of the stream.
    /// </summary>
    public uint[] CorrectionStatus;
}

/// <summary>
/// Context parameters for the Newton-Raphson Sqrt MegaKernel.
/// </summary>
public struct SqrtNewtonRaphson
{
    /// <summary>Core Newton-Raphson and NTT execution state.</summary>
    public NewtonRaphsonContext Core;
}

/// <summary>
/// Context parameters for the Newton-Raphson Reciprocal (DivRem) MegaKernel.
/// </summary>
public struct ReciprocalNewtonRaphson
{
    /// <summary>Core Newton-Raphson and NTT execution state.</summary>
    public NewtonRaphsonContext Core;
}

/// <summary>
/// Encapsulates a windowed slice of an exponent buffer for chunked modular exponentiation.
/// </summary>
public struct ExponentSliceContext
{
    /// <summary>Exponent word buffer.</summary>
    public uint[] Words;
    /// <summary>Total number of words in the exponent.</summary>
    public int TotalWords;
    /// <summary>Starting word index for exponentiation.</summary>
    public int StartWord;
    /// <summary>Ending word index for exponentiation.</summary>
    public int EndWord;
}

/// <summary>
/// Encapsulates context parameters for the NTT-Barrett Modular Exponentiation MegaKernel.
/// </summary>
public struct ModPowNttContext
{
    /// <summary>Core Newton-Raphson and NTT execution state.</summary>
    public NewtonRaphsonContext Core;
    /// <summary>Reciprocal modulus for Barrett reduction.</summary>
    public uint[] ReciprocalMod;
    /// <summary>Windowed exponent slice parameters.</summary>
    public ExponentSliceContext Exponent;
}

