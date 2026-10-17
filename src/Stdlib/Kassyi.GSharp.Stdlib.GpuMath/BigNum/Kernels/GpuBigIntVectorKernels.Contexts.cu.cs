namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class GpuBigIntVectorKernels
{
    public struct VectorSignConvertContext
    {
        [GpuReadOnly]
        public uint[] Src;

        [GpuReadOnly]
        public uint[] SignSrc;

        public uint[] Dest;
        public int NumWords;
        public int BatchSize;
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
        [GpuReadOnly]
        public uint[] A;

        [GpuReadOnly]
        public uint[] B;

        public uint[] Result;

        [GpuReadOnly]
        public uint[] SignA;

        [GpuReadOnly]
        public uint[] SignB;

        public uint[] SignResult;
        public int NumWordsA;
        public int NumWordsB;
        public int NumWordsResult;
        public uint OpSign;
        public uint ScalarSign;
        public bool IsScalar;
        public int BitOffsetA;
        public int BitOffsetB;
    }

    public struct VectorMulContext
    {
        [GpuReadOnly]
        public uint[] A;

        [GpuReadOnly]
        public uint[] B;

        public uint[] Result;

        [GpuReadOnly]
        public uint[] SignA;

        [GpuReadOnly]
        public uint[] SignB;

        public uint[] SignResult;
        public int NumWordsA;
        public int NumWordsB;
        public int NumWordsResult;
        public uint ScalarSign;
        public bool IsScalar;
    }

    public struct VectorDivModContext
    {
        [GpuReadOnly]
        public uint[] A;

        [GpuReadOnly]
        public uint[] B;

        public uint[] Result;

        [GpuReadOnly]
        public uint[] SignA;

        [GpuReadOnly]
        public uint[] SignB;

        public uint[] SignResult;
        public int NumWordsA;
        public int NumWordsB;
        public int NumWordsResult;
        public uint ScalarSign;
        public bool IsScalar;
        public bool IsModulo;
        public int PreShiftBits;
    }

    public struct VectorFloatAddSubContext
    {
        [GpuReadOnly]
        public uint[] A;

        [GpuReadOnly]
        public uint[] B;

        public uint[] Result;

        [GpuReadOnly]
        public uint[] ExpA;

        [GpuReadOnly]
        public uint[] ExpB;

        public uint[] ExpResult;

        [GpuReadOnly]
        public uint[] SignA;

        [GpuReadOnly]
        public uint[] SignB;

        public uint[] SignResult;
        public int NumWordsA;
        public int NumWordsB;
        public int NumWordsResult;
        public uint OpSign;
        public int ScalarExp;
        public uint ScalarSign;
        public bool IsScalar;
    }

    public struct VectorFloatMulContext
    {
        [GpuReadOnly]
        public uint[] A;

        [GpuReadOnly]
        public uint[] B;

        public uint[] Result;

        [GpuReadOnly]
        public uint[] ExpA;

        [GpuReadOnly]
        public uint[] ExpB;

        public uint[] ExpResult;

        [GpuReadOnly]
        public uint[] SignA;

        [GpuReadOnly]
        public uint[] SignB;

        public uint[] SignResult;
        public int NumWordsA;
        public int NumWordsB;
        public int NumWordsResult;
        public int ScalarExp;
        public uint ScalarSign;
        public bool IsScalar;
    }

    public struct VectorFloatDivContext
    {
        [GpuReadOnly]
        public uint[] A;

        [GpuReadOnly]
        public uint[] B;

        public uint[] Result;

        [GpuReadOnly]
        public uint[] ExpA;

        [GpuReadOnly]
        public uint[] ExpB;

        public uint[] ExpResult;

        [GpuReadOnly]
        public uint[] SignA;

        [GpuReadOnly]
        public uint[] SignB;

        public uint[] SignResult;
        public int NumWordsA;
        public int NumWordsB;
        public int NumWordsResult;
        public int ScalarExp;
        public uint ScalarSign;
        public bool IsScalar;
        public int PreShiftBits;
    }

    public struct VectorFloatCompareContext
    {
        [GpuReadOnly]
        public uint[] A;

        [GpuReadOnly]
        public uint[] B;

        public uint[] Result;

        [GpuReadOnly]
        public uint[] ExpA;

        [GpuReadOnly]
        public uint[] ExpB;

        [GpuReadOnly]
        public uint[] SignA;

        [GpuReadOnly]
        public uint[] SignB;

        public int NumWordsA;
        public int NumWordsB;
        public int ScalarExp;
        public uint ScalarSign;
        public bool IsScalar;
        public int OpType;
    }

    public struct VectorFloatReductionChunkContext
    {
        [GpuReadOnly]
        public uint[] A;

        [GpuReadOnly]
        public uint[] ExpA;

        [GpuReadOnly]
        public uint[] SignA;

        public uint[] Result;
        public uint[] ExpResult;
        public uint[] SignResult;
        public int NumWords;
        public int BatchSize;
        public int ChunkSize;
    }

    public struct VectorAccumulateContext
    {
        [GpuReadOnly]
        public uint[] Src;

        public int SrcOffset;
        public uint[] Dest;
        public int DestOffset;
        public int NumWords;
        public ulong TargetCmpBits;
    }

    public struct BlockBitLengthContext
    {
        [GpuReadOnly]
        public uint[] Data;

        public int BaseOffset;
        public int NumWords;
    }

    public struct ElementCompareContext
    {
        [GpuReadOnly]
        public uint[] A;

        public int BaseA;
        public int NumWordsA;
        public int ExpA;
        public uint SignA;

        [GpuReadOnly]
        public uint[] B;

        public int BaseB;
        public int NumWordsB;
        public int ExpB;
        public uint SignB;
    }

    public struct ShiftedLimbCompareContext
    {
        [GpuReadOnly]
        public uint[] A;

        public int BaseA;
        public int NumWordsA;
        public int ShiftA;

        [GpuReadOnly]
        public uint[] B;

        public int BaseB;
        public int NumWordsB;
        public int ShiftB;
        public int CompareWords;
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
        [GpuReadOnly]
        public uint[] Src;

        public int SrcOffset;
        public int SrcExp;
        public uint SrcSign;
        public uint[] Dest;
        public int DestOffset;
        public uint[] ExpResult;
        public uint[] SignResult;
        public int BlockIdx;
        public int NumWords;
    }
}
