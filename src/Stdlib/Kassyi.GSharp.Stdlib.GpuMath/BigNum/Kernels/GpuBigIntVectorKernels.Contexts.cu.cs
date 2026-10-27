namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class GpuBigIntVectorKernels
{
    public struct SignedVectorOperandReadOnlyContext
    {
        [GpuReadOnly]
        public uint[] Limbs;

        [GpuReadOnly]
        public uint[] Signs;

        public int NumWords;
    }

    public struct SignedVectorOutputContext
    {
        public uint[] Limbs;
        public uint[] Signs;
        public int NumWords;
    }

    public struct VectorBinaryOperationContext
    {
        public SignedVectorOperandReadOnlyContext Left;
        public SignedVectorOperandReadOnlyContext Right;
        public SignedVectorOutputContext Result;
        public uint ScalarSign;
        public bool IsScalar;
    }

    public struct FloatVectorExponentsContext
    {
        [GpuReadOnly]
        public uint[] ExpA;

        [GpuReadOnly]
        public uint[] ExpB;

        public uint[] ExpResult;
        public int ScalarExp;
    }

    public struct VectorFloatBinaryOperationContext
    {
        public VectorBinaryOperationContext Core;
        public FloatVectorExponentsContext Exponents;
    }

    public struct FloatVectorOperandReadOnlyContext
    {
        [GpuReadOnly]
        public uint[] Limbs;

        [GpuReadOnly]
        public uint[] Exponents;

        [GpuReadOnly]
        public uint[] Signs;

        public int NumWords;
    }

    public struct FloatVectorOutputContext
    {
        public uint[] Limbs;
        public uint[] Exponents;
        public uint[] Signs;
        public int NumWords;
    }

    public struct VectorSignConvertContext
    {
        public SignedVectorOperandReadOnlyContext Source;
        public uint[] Dest;

        /// <summary>
        /// The words per element of <see cref="Dest"/>; at least <c>Source.NumWords</c>, the extra upper words are the
        /// sign extension.
        /// </summary>
        public int DestWords;

        public int BatchSize;
    }

    /// <summary>
    /// Turns the accumulator of an integer reduction into a sign and a magnitude.
    /// </summary>
    public struct VectorReductionFinalizeContext
    {
        /// <summary>The output of the last pass: one accumulator of <see cref="SourceWords"/> words.</summary>
        [GpuReadOnly]
        public uint[] Source;

        /// <summary>Receives the magnitude, <see cref="ResultWords"/> words.</summary>
        public uint[] Destination;

        /// <summary>
        /// Two words: <c>[0]</c> bit 0 = the result is negative, bit 1 = the magnitude does not fit in
        /// <see cref="ResultWords"/> words; <c>[1]</c> = the number of reduced elements.
        /// </summary>
        public uint[] Header;

        public int SourceWords;
        public int ResultWords;

        /// <summary>The number of reduced elements; 0 writes a zero result.</summary>
        public int Count;

        /// <summary>The accumulator is offset binary (Max / Min): its top bit is flipped.</summary>
        public bool OffsetBinary;
    }

    public struct VectorReductionChunkContext
    {
        [GpuReadOnly]
        public uint[] A;

        public uint[] Result;
        public int NumWords;
        public int BatchSize;
        public int ChunkSize;
    }

    public struct VectorAddSubContext
    {
        public VectorBinaryOperationContext Op;
        public uint OpSign;
        public int BitOffsetA;
        public int BitOffsetB;
    }

    public struct VectorMulContext
    {
        public VectorBinaryOperationContext Op;
    }

    public struct VectorDivModContext
    {
        public VectorBinaryOperationContext Op;
        public bool IsModulo;
        public int PreShiftBits;
    }

    public struct VectorFloatAddSubContext
    {
        public VectorFloatBinaryOperationContext FloatOp;
        public uint OpSign;
    }

    public struct VectorFloatMulContext
    {
        public VectorFloatBinaryOperationContext FloatOp;
    }

    public struct VectorFloatDivContext
    {
        public VectorFloatBinaryOperationContext FloatOp;
        public int PreShiftBits;
    }

    public struct VectorFloatCompareContext
    {
        public FloatVectorOperandReadOnlyContext Left;
        public FloatVectorOperandReadOnlyContext Right;
        public uint[] Result;
        public int ScalarExp;
        public uint ScalarSign;
        public bool IsScalar;
        public int OpType;
    }

    public struct VectorFloatReductionChunkContext
    {
        public FloatVectorOperandReadOnlyContext Input;
        public FloatVectorOutputContext Output;
        public int BatchSize;
        public int ChunkSize;
    }

    public struct BlockBitLengthContext
    {
        [GpuReadOnly]
        public uint[] Data;

        public int BaseOffset;
        public int NumWords;
    }

    public struct ShiftedLimbSliceContext
    {
        public BlockBitLengthContext Slice;
        public int Shift;
    }

    public struct ShiftedLimbCompareContext
    {
        public ShiftedLimbSliceContext Left;
        public ShiftedLimbSliceContext Right;
        public int CompareWords;
    }

    public struct FloatElementSliceContext
    {
        public BlockBitLengthContext Slice;
        public int Exp;
        public uint Sign;
    }

    public struct ElementCompareContext
    {
        public FloatElementSliceContext Left;
        public FloatElementSliceContext Right;
    }

    public struct VectorAccumulateContext
    {
        public BlockBitLengthContext Source;
        public uint[] Dest;
        public int DestOffset;
        public ulong TargetCmpBits;
    }

    public struct FloatAlignmentInput
    {
        public int ExpA;
        public int ExpB;
        public uint BitLenA;
        public uint BitLenB;
        public int NumWords;
        public int BitOffsetA;
        public int BitOffsetB;
        public int OutExp;
    }

    public struct MutableElementSliceContext
    {
        public uint[] Buffer;
        public int BaseOffset;
        public int NumWords;
        public int BitOffset;
    }

    public struct FloatAccumulateStepContext
    {
        public FloatElementSliceContext Source;
        public uint[] Dest;
        public int DestOffset;
        public uint[] ExpResult;
        public uint[] SignResult;
        public int BlockIdx;
    }
}
