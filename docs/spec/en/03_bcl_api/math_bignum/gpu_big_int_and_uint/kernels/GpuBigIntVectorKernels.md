---
title: "GpuBigIntVectorKernels Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Batched multi-word vector arithmetic, representation conversion, division/modulo, and parallel reduction kernels for GpuBigIntArray and GpuBigFloatArray."
---

## GpuBigIntVectorKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and device functions for batched vector operations on `GpuBigIntArray` and `GpuBigFloatArray`, including representation conversions (sign-magnitude to two's complement and offset binary), scalar and zip arithmetic (`Add`, `Sub`, `Mul`, `Div`, `Mod`), element-wise floating-point comparisons, boolean quantifier reductions, and multi-pass tree reductions (`Sum`, `Min`, `Max`).

```csharp
[SkipSnapshotConsistency("Tested in separate suites")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntVectorKernels
```

## Fields

| Name | Description |
| --- | --- |
| `VectorSignMagnitudeToTwosComplementKernelHandle` | Cached native CUDA kernel handle (`IntPtr`) for `VectorSignMagnitudeToTwosComplementKernel`. |
| `VectorSignMagnitudeToOffsetBinaryKernelHandle` | Cached native CUDA kernel handle (`IntPtr`) for `VectorSignMagnitudeToOffsetBinaryKernel`. |

## Nested Context Structs

All vector kernels and device functions accept flattened value-type context structs to respect the CUDA kernel parameter space limit and avoid redundant parameter marshaling:

```csharp
public struct SignedVectorOperandReadOnlyContext
{
    [GpuReadOnly] public uint[] Limbs;
    [GpuReadOnly] public uint[] Signs;
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
    [GpuReadOnly] public uint[] ExpA;
    [GpuReadOnly] public uint[] ExpB;
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
    [GpuReadOnly] public uint[] Limbs;
    [GpuReadOnly] public uint[] Exponents;
    [GpuReadOnly] public uint[] Signs;
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
    public int DestWords;
    public int BatchSize;
}

public struct ElementCountContext
{
    [GpuReadOnly] public uint[] Base;
    public int Skip;
    public int Limit;
}

public struct VectorReductionFinalizeContext
{
    [GpuReadOnly] public uint[] Source;
    public uint[] Destination;
    public uint[] Header;
    public int SourceWords;
    public int ResultWords;
    public ElementCountContext Count;
    public bool OffsetBinary;
}

public struct VectorReductionChunkContext
{
    [GpuReadOnly] public uint[] A;
    public uint[] Result;
    public int NumWords;
    public ElementCountContext Count;
    public int PassIndex;
    public int ChunkSize;
}

public struct VectorBoolReductionContext
{
    [GpuReadOnly] public uint[] Flags;
    public uint[] Result;
    public ElementCountContext Count;
}

public struct CountedCopyContext
{
    public uint[] Destination;
    [GpuReadOnly] public uint[] Source;
    public int WordsPerElement;
    public ElementCountContext Count;
}

public struct ElementCountWriteContext
{
    public uint[] Destination;
    public int DestinationWords;
    public ElementCountContext Count;
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
    public uint[] ZeroDivisor;
    public uint[] Scratch;
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
    public uint[] ZeroDivisor;
    public uint[] Scratch;
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
    [GpuReadOnly] public uint[] Data;
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
```

## Methods

### Representation Conversion & Bit-Shift Primitives

| Name | Description |
| --- | --- |
| [PropagateBlockCarry(uint, uint, uint, SharedMemory\<uint\>, int, int, int, out uint)](#propagateblockcarryuint-uint-uint-sharedmemoryuint-int-int-int-out-uint) | A GPU device function (`[GpuDeviceFunction]`) that propagates generate/propagate carry flags across a thread block using warp-level Kogge-Stone scans. |
| [GetVectorShiftedWord(uint[], int, int, int, int)](#getvectorshiftedworduint-int-int-int-int) | A GPU device function (`[GpuDeviceFunction]`) that extracts a 32-bit word at `destIdx` from a multi-word slice shifted by `bitOffset` bits. |
| [DeviceVectorSignMagnitudeToTwosComplement(VectorSignConvertContext)](#devicevectorsignmagnitudetotwoscomplementvectorsignconvertcontext) | A GPU device function (`[GpuDeviceFunction]`) that converts a batch of sign-magnitude integers to two's complement representation in parallel. |
| [VectorSignMagnitudeToTwosComplementKernel(VectorSignConvertContext)](#vectorsignmagnitudetotwoscomplementkernelvectorsignconvertcontext) | A GPU kernel (`[GpuKernel]`) that converts a batch of sign-magnitude integers to two's complement representation. |
| [VectorSignMagnitudeToOffsetBinaryKernel(VectorSignConvertContext)](#vectorsignmagnitudetooffsetbinarykernelvectorsignconvertcontext) | A GPU kernel (`[GpuKernel]`) that converts a batch of sign-magnitude integers to offset-binary representation (for signed `Min`/`Max` reductions). |

### Integer & Float Vector Addition / Subtraction

| Name | Description |
| --- | --- |
| [VectorAddScalarKernel(VectorAddSubContext)](#vectoraddscalarkernelvectoraddsubcontext) | A GPU kernel (`[GpuKernel]`) that adds a scalar `GpuBigInt` to each element of a vector. |
| [VectorSubScalarKernel(VectorAddSubContext)](#vectorsubscalarkernelvectoraddsubcontext) | A GPU kernel (`[GpuKernel]`) that subtracts a scalar `GpuBigInt` from each element of a vector. |
| [VectorZipAddKernel(VectorAddSubContext)](#vectorzipaddkernelvectoraddsubcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise addition of two `GpuBigIntArray` vectors. |
| [VectorZipSubKernel(VectorAddSubContext)](#vectorzipsubkernelvectoraddsubcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise subtraction of two `GpuBigIntArray` vectors. |
| [VectorFloatAddScalarKernel(VectorFloatAddSubContext)](#vectorfloataddscalarkernelvectorfloataddsubcontext) | A GPU kernel (`[GpuKernel]`) that adds a scalar `GpuBigFloat` to each element of a float vector with radix alignment and normalization. |
| [VectorFloatSubScalarKernel(VectorFloatAddSubContext)](#vectorfloatsubscalarkernelvectorfloatsubcontext) | A GPU kernel (`[GpuKernel]`) that subtracts a scalar `GpuBigFloat` from each element of a float vector with radix alignment and normalization. |
| [VectorFloatZipAddKernel(VectorFloatAddSubContext)](#vectorfloatzipaddkernelvectorfloataddsubcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise addition of two `GpuBigFloatArray` vectors. |
| [VectorFloatZipSubKernel(VectorFloatAddSubContext)](#vectorfloatzipsubkernelvectorfloataddsubcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise subtraction of two `GpuBigFloatArray` vectors. |
| [DeviceVectorAddSubParallel(VectorAddSubContext, bool)](#devicevectoraddsubparallelvectoraddsubcontext-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs signed magnitude-aware addition or subtraction for one vector element per block. |
| [DeviceVectorAddSubAlignedCore(VectorAddSubContext, bool)](#devicevectoraddsubalignedcorevectoraddsubcontext-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs bit-shifted signed addition or subtraction for radix-aligned operands. |
| [DeviceVectorFloatAddSubParallel(VectorFloatAddSubContext, bool)](#devicevectorfloataddsubparallelvectorfloataddsubcontext-bool) | A GPU device function (`[GpuDeviceFunction]`) that aligns exponents, adds or subtracts significands, and normalizes the floating-point result for one element per block. |

### Integer & Float Vector Multiplication

| Name | Description |
| --- | --- |
| [VectorMulScalarKernel(VectorMulContext)](#vectormulscalarkernelvectormulcontext) | A GPU kernel (`[GpuKernel]`) that multiplies each element of an integer vector by a scalar. |
| [VectorZipMulKernel(VectorMulContext)](#vectorzipmulkernelvectormulcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise multiplication of two `GpuBigIntArray` vectors. |
| [DeviceVectorMulVectorParallel(VectorMulContext, bool)](#devicevectormulvectorparallelvectormulcontext-bool) | A GPU device function (`[GpuDeviceFunction]`) that computes the multi-word product of two vector elements (or an element and a scalar) within a block. |
| [VectorFloatMulScalarKernel(VectorFloatMulContext)](#vectorfloatmulscalarkernelvectorfloatmulcontext) | A GPU kernel (`[GpuKernel]`) that multiplies each element of a float vector by a scalar and normalizes the result. |
| [VectorFloatZipMulKernel(VectorFloatMulContext)](#vectorfloatzipmulkernelvectorfloatmulcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise multiplication of two `GpuBigFloatArray` vectors. |
| [DeviceVectorFloatMulParallel(VectorFloatMulContext)](#devicevectorfloatmulparallelvectorfloatmulcontext) | A GPU device function (`[GpuDeviceFunction]`) that multiplies two floating-point significands, adds their exponents, and normalizes the product to the output width. |

### Integer & Float Vector Division / Modulo

| Name | Description |
| --- | --- |
| [VectorDivScalarKernel(VectorDivModContext)](#vectordivscalarkernelvectordivmodcontext) | A GPU kernel (`[GpuKernel]`) that divides each element of an integer vector by a scalar divisor. |
| [VectorModScalarKernel(VectorDivModContext)](#vectormodscalarkernelvectordivmodcontext) | A GPU kernel (`[GpuKernel]`) that computes the remainder of each element of an integer vector divided by a scalar divisor. |
| [VectorZipDivKernel(VectorDivModContext)](#vectorzipdivkernelvectordivmodcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise division of two `GpuBigIntArray` vectors. |
| [VectorZipModKernel(VectorDivModContext)](#vectorzipmodkernelvectordivmodcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise modulo of two `GpuBigIntArray` vectors. |
| [DeviceVectorDivModParallel(VectorDivModContext)](#devicevectordivmodparallelvectordivmodcontext) | A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel restoring bitwise division or modulo for one vector element per block. |
| [VectorFloatDivScalarKernel(VectorFloatDivContext)](#vectorfloatdivscalarkernelvectorfloatdivcontext) | A GPU kernel (`[GpuKernel]`) that divides each element of a float vector by a scalar divisor. |
| [VectorFloatZipDivKernel(VectorFloatDivContext)](#vectorfloatzipdivkernelvectorfloatdivcontext) | A GPU kernel (`[GpuKernel]`) that performs element-wise division of two `GpuBigFloatArray` vectors. |
| [DeviceVectorFloatDivParallel(VectorFloatDivContext)](#devicevectorfloatdivparallelvectorfloatdivcontext) | A GPU device function (`[GpuDeviceFunction]`) that normalizes dividend and divisor bit lengths, computes the fractional quotient significand, and writes the normalized exponent and sign. |

### Reductions, Comparisons & Count Helpers

| Name | Description |
| --- | --- |
| [ReduceWarpMaxUInt64(uint, ulong)](#reducewarpmaxuint64uint-ulong) | A GPU device function (`[GpuDeviceFunction]`) that computes the warp-wide maximum of a 64-bit unsigned integer using `__shfl_xor_sync`. |
| [ReduceBlockMaxUInt64(ulong, SharedMemory\<ulong\>)](#reduceblockmaxuint64ulong-sharedmemoryulong) | A GPU device function (`[GpuDeviceFunction]`) that computes the block-wide maximum of a 64-bit unsigned integer across warps. |
| [CompareShiftedLimbsBlockParallel(ShiftedLimbCompareContext, SharedMemory\<ulong\>)](#compareshiftedlimbsblockparallelshiftedlimbcomparecontext-sharedmemoryulong) | A GPU device function (`[GpuDeviceFunction]`) that compares two bit-shifted multi-word slices in parallel across a thread block (`1` if left > right, `2` if left < right, `0` if equal). |
| [CopyElementWordsBlockParallel(VectorAccumulateContext)](#copyelementwordsblockparallelvectoraccumulatecontext) | A GPU device function (`[GpuDeviceFunction]`) that copies `Source.NumWords` words from `Source.Data` into `Dest` in parallel across the block. |
| [PassBatchSize(ElementCountContext, int, int)](#passbatchsizeelementcountcontext-int-int) | A GPU device function (`[GpuDeviceFunction]`) that computes the active element count entering reduction pass `passIndex` for a given `chunkSize`. |
| [VectorSumReductionChunkKernel(VectorReductionChunkContext)](#vectorsumreductionchunkkernelvectorreductionchunkcontext) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch by summation. |
| [DeviceVectorSumReductionChunk(VectorReductionChunkContext)](#devicevectorsumreductionchunkvectorreductionchunkcontext) | A GPU device function (`[GpuDeviceFunction]`) that accumulates a chunk of multi-word integers into a block-level sum buffer. |
| [DeviceVectorAddAccumulateParallel(VectorAccumulateContext)](#devicevectoraddaccumulateparallelvectoraccumulatecontext) | A GPU device function (`[GpuDeviceFunction]`) that adds a multi-word integer slice into a destination slice using Kogge-Stone carry lookahead. |
| [VectorReductionFinalizeKernel(VectorReductionFinalizeContext)](#vectorreductionfinalizekernelvectorreductionfinalizecontext) | A GPU kernel (`[GpuKernel]`) that converts the final reduced two's complement or offset-binary value back to sign-magnitude representation and writes `Header` (`[bitLength, isNegative]`). |
| [VectorMaxReductionChunkKernel(VectorReductionChunkContext)](#vectormaxreductionchunkkernelvectorreductionchunkcontext) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch to their maximum value. |
| [DeviceVectorMaxReductionChunk(VectorReductionChunkContext)](#devicevectormaxreductionchunkvectorreductionchunkcontext) | A GPU device function (`[GpuDeviceFunction]`) that computes the maximum multi-word integer across a chunk. |
| [VectorMinReductionChunkKernel(VectorReductionChunkContext)](#vectorminreductionchunkkernelvectorreductionchunkcontext) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch to their minimum value. |
| [DeviceVectorMinReductionChunk(VectorReductionChunkContext)](#devicevectorminreductionchunkvectorreductionchunkcontext) | A GPU device function (`[GpuDeviceFunction]`) that computes the minimum multi-word integer across a chunk. |
| [DeviceVectorCompareReductionChunk(VectorReductionChunkContext, ulong)](#devicevectorcomparereductionchunkvectorreductionchunkcontext-ulong) | A GPU device function (`[GpuDeviceFunction]`) that reduces a chunk of unsigned/offset-binary elements according to `targetCmpBits` (`1` for maximum, `2` for minimum). |
| [DeviceVectorCompareAccumulateParallel(VectorAccumulateContext)](#devicevectorcompareaccumulateparallelvectoraccumulatecontext) | A GPU device function (`[GpuDeviceFunction]`) that compares `acc.Source` against `acc.Dest` and overwrites `acc.Dest` when the comparison matches `acc.TargetCmpBits`. |
| [ComputeBlockBitLength(BlockBitLengthContext, SharedMemory\<ulong\>)](#computeblockbitlengthblockbitlengthcontext-sharedmemoryulong) | A GPU device function (`[GpuDeviceFunction]`) that computes the exact bit length of a multi-word slice across the thread block. |
| [DeviceVectorFloatCompareElement(ElementCompareContext)](#devicevectorfloatcompareelementelementcomparecontext) | A GPU device function (`[GpuDeviceFunction]`) that compares two signed floating-point elements (`1` if left > right, `2` if left < right, `0` if equal). |
| [EvaluateCompareOperator(uint, int)](#evaluatecompareoperatoruint-int) | A GPU device function (`[GpuDeviceFunction]`) that evaluates a `VectorCompareOperator` (`opType`) against a 3-way comparison result `cmpRes`. |
| [VectorFloatCompareKernel(VectorFloatCompareContext)](#vectorfloatcomparekernelvectorfloatcomparecontext) | A GPU kernel (`[GpuKernel]`) that compares each element of a float vector against a scalar or another float vector and writes `1u` or `0u` flags. |
| [InitFloatReductionChunkElement(VectorFloatReductionChunkContext)](#initfloatreductionchunkelementvectorfloatreductionchunkcontext) | A GPU device function (`[GpuDeviceFunction]`) that copies the first element of a float reduction chunk into the block output slot and returns the chunk start index. |
| [VectorFloatMaxReductionChunkKernel(VectorFloatReductionChunkContext)](#vectorfloatmaxreductionchunkkernelvectorfloatreductionchunkcontext) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a `GpuBigFloatArray` batch to their maximum value. |
| [VectorFloatMinReductionChunkKernel(VectorFloatReductionChunkContext)](#vectorfloatminreductionchunkkernelvectorfloatreductionchunkcontext) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a `GpuBigFloatArray` batch to their minimum value. |
| [DeviceVectorFloatCompareReductionChunk(VectorFloatReductionChunkContext, uint)](#devicevectorfloatcomparereductionchunkvectorfloatreductionchunkcontext-uint) | A GPU device function (`[GpuDeviceFunction]`) that reduces a chunk of floating-point elements to their maximum (`targetCmpBits == 1`) or minimum (`targetCmpBits == 2`). |
| [ComputeFloatAlignmentOffsets(ref FloatAlignmentInput)](#computefloatalignmentoffsetsref-floatalignmentinput) | A GPU device function (`[GpuDeviceFunction]`) that computes the significand bit shifts and output exponent for radix-aligned floating-point addition. |
| [ShiftElementInPlaceBlockParallel(MutableElementSliceContext)](#shiftelementinplaceblockparallelmutableelementslicecontext) | A GPU device function (`[GpuDeviceFunction]`) that shifts a multi-word slice in place by `slice.BitOffset` bits across the thread block. |
| [NegateElementInPlaceBlockParallel(MutableElementSliceContext)](#negateelementinplaceblockparallelmutableelementslicecontext) | A GPU device function (`[GpuDeviceFunction]`) that two's-complement negates a multi-word slice in place across the thread block. |
| [AccumulateFloatElementBlockParallel(FloatAccumulateStepContext)](#accumulatefloatelementblockparallelfloataccumulatestepcontext) | A GPU device function (`[GpuDeviceFunction]`) that aligns and accumulates one floating-point element into the running chunk accumulator. |
| [VectorFloatSumReductionChunkKernel(VectorFloatReductionChunkContext)](#vectorfloatsumreductionchunkkernelvectorfloatreductionchunkcontext) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a `GpuBigFloatArray` batch by summation. |
| [DeviceVectorFloatSumReductionChunk(VectorFloatReductionChunkContext)](#devicevectorfloatsumreductionchunkvectorfloatreductionchunkcontext) | A GPU device function (`[GpuDeviceFunction]`) that sums a chunk of floating-point elements with radix alignment and normalization. |
| [VectorAnyTrueKernel(VectorBoolReductionContext)](#vectoranytruekernelvectorboolreductioncontext) | A GPU kernel (`[GpuKernel]`) that sets `ctx.Result[0] = 1u` if any active predicate flag in `ctx.Flags` is non-zero. |
| [VectorAllTrueKernel(VectorBoolReductionContext)](#vectoralltruekernelvectorboolreductioncontext) | A GPU kernel (`[GpuKernel]`) that sets `ctx.Result[0] = 0u` if any active predicate flag in `ctx.Flags` is zero. |
| [WriteElementCountKernel(ElementCountWriteContext)](#writeelementcountkernelelementcountwritecontext) | A GPU kernel (`[GpuKernel]`) that resolves `ctx.Count` on the device and writes the active count as a multi-word integer into `ctx.Destination`. |
| [CopyCountedElementsKernel(CountedCopyContext)](#copycountedelementskernelcountedcopycontext) | A GPU kernel (`[GpuKernel]`) that copies the active elements (`ResolveCount(ctx.Count)`) from `ctx.Source` (offset by `ctx.Count.Skip`) into `ctx.Destination`. |

---

## PropagateBlockCarry(uint, uint, uint, SharedMemory\<uint\>, int, int, int, out uint)

Propagates generate (`g`) and propagate (`p`) carry flags across a thread block using warp-level Kogge-Stone prefix scans and `sharedCarry`, returning the carry into each thread's word and outputting `nextBlockCarryIn`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint PropagateBlockCarry(
    uint g,
    uint p,
    uint blockCarryIn,
    SharedMemory<uint> sharedCarry,
    int laneId,
    int warpId,
    int bDim,
    out uint nextBlockCarryIn);
```

## GetVectorShiftedWord(uint[], int, int, int, int)

Extracts the 32-bit word at `destIdx` from the `numWords`-word slice starting at `baseOffset` in `data` after a logical bit shift of `bitOffset` bits (positive shifts left, negative shifts right).

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint GetVectorShiftedWord(
    [GpuReadOnly] uint[] data,
    int baseOffset,
    int numWords,
    int destIdx,
    int bitOffset);
```

## DeviceVectorSignMagnitudeToTwosComplement(VectorSignConvertContext)

Converts a batch of sign-magnitude integers to two's complement representation in parallel across blocks.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorSignMagnitudeToTwosComplement(VectorSignConvertContext ctx);
```

## VectorSignMagnitudeToTwosComplementKernel(VectorSignConvertContext)

Kernel entry point that converts a batch of sign-magnitude integers to two's complement representation.

```csharp
[GpuKernel]
public static void VectorSignMagnitudeToTwosComplementKernel(VectorSignConvertContext ctx);
```

## VectorSignMagnitudeToOffsetBinaryKernel(VectorSignConvertContext)

Converts a batch of sign-magnitude integers to offset-binary representation (inverting bits of negative values and setting the MSB of non-negative values) so signed `Min`/`Max` reductions can use unsigned comparisons.

```csharp
[GpuKernel]
public static void VectorSignMagnitudeToOffsetBinaryKernel(VectorSignConvertContext ctx);
```

## VectorAddScalarKernel(VectorAddSubContext)

Adds a scalar `GpuBigInt` to each element in a vector batch.

```csharp
[GpuKernel]
public static void VectorAddScalarKernel(VectorAddSubContext ctx);
```

## VectorSubScalarKernel(VectorAddSubContext)

Subtracts a scalar `GpuBigInt` from each element in a vector batch.

```csharp
[GpuKernel]
public static void VectorSubScalarKernel(VectorAddSubContext ctx);
```

## VectorZipAddKernel(VectorAddSubContext)

Performs element-wise addition of two `GpuBigIntArray` batches.

```csharp
[GpuKernel]
public static void VectorZipAddKernel(VectorAddSubContext ctx);
```

## VectorZipSubKernel(VectorAddSubContext)

Performs element-wise subtraction of two `GpuBigIntArray` batches.

```csharp
[GpuKernel]
public static void VectorZipSubKernel(VectorAddSubContext ctx);
```

## VectorFloatAddScalarKernel(VectorFloatAddSubContext)

Adds a scalar `GpuBigFloat` to each element of a `GpuBigFloatArray` batch.

```csharp
[GpuKernel]
public static void VectorFloatAddScalarKernel(VectorFloatAddSubContext ctx);
```

## VectorFloatSubScalarKernel(VectorFloatAddSubContext)

Subtracts a scalar `GpuBigFloat` from each element of a `GpuBigFloatArray` batch.

```csharp
[GpuKernel]
public static void VectorFloatSubScalarKernel(VectorFloatAddSubContext ctx);
```

## VectorFloatZipAddKernel(VectorFloatAddSubContext)

Performs element-wise addition of two `GpuBigFloatArray` batches.

```csharp
[GpuKernel]
public static void VectorFloatZipAddKernel(VectorFloatAddSubContext ctx);
```

## VectorFloatZipSubKernel(VectorFloatAddSubContext)

Performs element-wise subtraction of two `GpuBigFloatArray` batches.

```csharp
[GpuKernel]
public static void VectorFloatZipSubKernel(VectorFloatAddSubContext ctx);
```

## DeviceVectorAddSubParallel(VectorAddSubContext, bool)

Performs signed magnitude-aware addition or subtraction for one vector element per block with unshifted operands (`BitOffsetA = 0`, `BitOffsetB = 0`).

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorAddSubParallel(VectorAddSubContext ctx, bool syncThreads = true);
```

## DeviceVectorAddSubAlignedCore(VectorAddSubContext, bool)

Performs signed magnitude-aware addition or subtraction for one vector element per block, applying `ctx.BitOffsetA` and `ctx.BitOffsetB` bit shifts on the fly.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorAddSubAlignedCore(VectorAddSubContext ctx, bool syncThreads = true);
```

## DeviceVectorFloatAddSubParallel(VectorFloatAddSubContext, bool)

Aligns the significands of two floating-point operands by their exponents and bit lengths, executes `DeviceVectorAddSubAlignedCore`, and normalizes the resulting significand, exponent, and sign.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorFloatAddSubParallel(VectorFloatAddSubContext ctx, bool syncThreads = true);
```

## VectorMulScalarKernel(VectorMulContext)

Multiplies each element of a `GpuBigIntArray` batch by a scalar `GpuBigInt`.

```csharp
[GpuKernel]
public static void VectorMulScalarKernel(VectorMulContext ctx);
```

## VectorZipMulKernel(VectorMulContext)

Performs element-wise multiplication of two `GpuBigIntArray` batches.

```csharp
[GpuKernel]
public static void VectorZipMulKernel(VectorMulContext ctx);
```

## DeviceVectorMulVectorParallel(VectorMulContext, bool)

Performs parallel multi-word multiplication of two vector elements (or a vector element and a scalar when `ctx.Op.IsScalar` is `true`) within a block. Computes the product up to `ctx.Op.Result.NumWords` words: when `ctx.Op.Result.NumWords >= ctx.Op.Left.NumWords + ctx.Op.Right.NumWords` (as allocated by `SelectMultiply` and `ZipMultiply`), the result holds the full exact product without overflow or truncation.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMulVectorParallel(VectorMulContext ctx, bool syncThreads = true);
```

## VectorFloatMulScalarKernel(VectorFloatMulContext)

Multiplies each element of a `GpuBigFloatArray` batch by a scalar `GpuBigFloat`.

```csharp
[GpuKernel]
public static void VectorFloatMulScalarKernel(VectorFloatMulContext ctx);
```

## VectorFloatZipMulKernel(VectorFloatMulContext)

Performs element-wise multiplication of two `GpuBigFloatArray` batches.

```csharp
[GpuKernel]
public static void VectorFloatZipMulKernel(VectorFloatMulContext ctx);
```

## DeviceVectorFloatMulParallel(VectorFloatMulContext)

Multiplies two floating-point significands into `ctx.FloatOp.Core.Result`, sums their exponents, and normalizes the product toward zero to the output element width.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorFloatMulParallel(VectorFloatMulContext ctx);
```

## VectorDivScalarKernel(VectorDivModContext)

Divides each element of a `GpuBigIntArray` batch by a scalar divisor.

```csharp
[GpuKernel]
public static void VectorDivScalarKernel(VectorDivModContext ctx);
```

## VectorModScalarKernel(VectorDivModContext)

Computes the remainder of each element of a `GpuBigIntArray` batch divided by a scalar divisor.

```csharp
[GpuKernel]
public static void VectorModScalarKernel(VectorDivModContext ctx);
```

## VectorZipDivKernel(VectorDivModContext)

Performs element-wise division of two `GpuBigIntArray` batches.

```csharp
[GpuKernel]
public static void VectorZipDivKernel(VectorDivModContext ctx);
```

## VectorZipModKernel(VectorDivModContext)

Performs element-wise modulo of two `GpuBigIntArray` batches.

```csharp
[GpuKernel]
public static void VectorZipModKernel(VectorDivModContext ctx);
```

## DeviceVectorDivModParallel(VectorDivModContext)

Performs block-parallel restoring bitwise division or modulo (`ctx.IsModulo`) for one vector element per block, supporting an optional left pre-shift (`ctx.PreShiftBits`) of the dividend for floating-point significand division. An element whose divisor is zero gets a zero result, and its index is recorded in `ctx.ZeroDivisor[0]` as an atomic maximum of `~index`, so that the word, cleared to 0 before the launch, ends as the complement of the lowest such index; the host reads it and throws `DivideByZeroException`. A scalar launch, whose divisor the host has checked, passes no word. The remainder and the quotient are kept in two shared buffers of `DivModSharedWords` (512) words; when the widest of the result and the operands has more words, they are kept instead in `ctx.Scratch`, which holds `2 * evalWords` words per block (the remainder, then the quotient) and which the host allocates only for such widths.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorDivModParallel(VectorDivModContext ctx);
```

## VectorFloatDivScalarKernel(VectorFloatDivContext)

Divides each element of a `GpuBigFloatArray` batch by a scalar `GpuBigFloat`.

```csharp
[GpuKernel]
public static void VectorFloatDivScalarKernel(VectorFloatDivContext ctx);
```

## VectorFloatZipDivKernel(VectorFloatDivContext)

Performs element-wise division of two `GpuBigFloatArray` batches.

```csharp
[GpuKernel]
public static void VectorFloatZipDivKernel(VectorFloatDivContext ctx);
```

## DeviceVectorFloatDivParallel(VectorFloatDivContext)

Computes the bit lengths of the dividend and divisor significands, invokes `DeviceVectorDivModParallel` with `ctx.PreShiftBits` to produce a full-precision quotient significand, and normalizes the result exponent and sign.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorFloatDivParallel(VectorFloatDivContext ctx);
```

## ReduceWarpMaxUInt64(uint, ulong)

Computes the warp-wide maximum of `val` across active lanes in `mask` using butterfly XOR shuffle reductions.

```csharp
[GpuDeviceFunction]
[GpuInline]
public static ulong ReduceWarpMaxUInt64(uint mask, ulong val);
```

## ReduceBlockMaxUInt64(ulong, SharedMemory\<ulong\>)

Computes the block-wide maximum of `myVal` across all warps in the thread block using `warpMax` shared memory.

```csharp
[GpuDeviceFunction]
[GpuInline]
public static ulong ReduceBlockMaxUInt64(ulong myVal, SharedMemory<ulong> warpMax);
```

## CompareShiftedLimbsBlockParallel(ShiftedLimbCompareContext, SharedMemory\<ulong\>)

Compares two bit-shifted multi-word slices across a thread block by packing the highest differing word index (`idx + 1`) and outcome (`1` if left > right, `2` if left < right) into a 64-bit integer and reducing via `ReduceBlockMaxUInt64`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint CompareShiftedLimbsBlockParallel(
    ShiftedLimbCompareContext ctx,
    SharedMemory<ulong> warpMax);
```

## CopyElementWordsBlockParallel(VectorAccumulateContext)

Copies `copyCtx.Source.NumWords` 32-bit words from `copyCtx.Source.Data` (at `copyCtx.Source.BaseOffset`) to `copyCtx.Dest` (at `copyCtx.DestOffset`) in parallel across the thread block.

```csharp
[GpuDeviceFunction]
[GpuInline]
public static void CopyElementWordsBlockParallel(VectorAccumulateContext copyCtx);
```

## PassBatchSize(ElementCountContext, int, int)

Computes the number of active input elements for reduction pass `passIndex` by resolving `count` on pass `0` and repeatedly ceiling-dividing by `chunkSize` for subsequent passes.

```csharp
[GpuDeviceFunction]
[GpuInline]
public static int PassBatchSize(ElementCountContext count, int passIndex, int chunkSize);
```

## VectorSumReductionChunkKernel(VectorReductionChunkContext)

Reduces chunks of a two's-complement multi-word integer batch by summation.

```csharp
[GpuKernel]
public static void VectorSumReductionChunkKernel(VectorReductionChunkContext ctx);
```

## DeviceVectorSumReductionChunk(VectorReductionChunkContext)

Accumulates a chunk of `ctx.ChunkSize` multi-word integers into a block-level sum slot in `ctx.Result`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorSumReductionChunk(VectorReductionChunkContext ctx);
```

## DeviceVectorAddAccumulateParallel(VectorAccumulateContext)

Adds the multi-word integer slice `acc.Source` into `acc.Dest` (at `acc.DestOffset`) in place using Kogge-Stone carry lookahead.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorAddAccumulateParallel(VectorAccumulateContext acc);
```

## VectorReductionFinalizeKernel(VectorReductionFinalizeContext)

Converts the single reduced two's-complement or offset-binary integer in `ctx.Source` back to sign-magnitude representation in `ctx.Destination` and writes `[bitLength, isNegative]` into `ctx.Header`. When `ctx.Count.Base` is non-null and resolves to `0` elements, zeroes out the destination and header.

```csharp
[GpuKernel]
public static void VectorReductionFinalizeKernel(VectorReductionFinalizeContext ctx);
```

## VectorMaxReductionChunkKernel(VectorReductionChunkContext)

Reduces chunks of an offset-binary multi-word integer batch to their maximum value.

```csharp
[GpuKernel]
public static void VectorMaxReductionChunkKernel(VectorReductionChunkContext ctx);
```

## DeviceVectorMaxReductionChunk(VectorReductionChunkContext)

Computes the maximum multi-word integer across a chunk by invoking `DeviceVectorCompareReductionChunk(ctx, 1UL)`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMaxReductionChunk(VectorReductionChunkContext ctx);
```

## VectorMinReductionChunkKernel(VectorReductionChunkContext)

Reduces chunks of an offset-binary multi-word integer batch to their minimum value.

```csharp
[GpuKernel]
public static void VectorMinReductionChunkKernel(VectorReductionChunkContext ctx);
```

## DeviceVectorMinReductionChunk(VectorReductionChunkContext)

Computes the minimum multi-word integer across a chunk by invoking `DeviceVectorCompareReductionChunk(ctx, 2UL)`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMinReductionChunk(VectorReductionChunkContext ctx);
```

## DeviceVectorCompareReductionChunk(VectorReductionChunkContext, ulong)

Reduces a chunk of multi-word integers in `ctx.A` into `ctx.Result` by keeping the element that matches `targetCmpBits` (`1UL` for maximum, `2UL` for minimum).

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorCompareReductionChunk(VectorReductionChunkContext ctx, ulong targetCmpBits);
```

## DeviceVectorCompareAccumulateParallel(VectorAccumulateContext)

Compares the candidate element `acc.Source` against the current accumulator in `acc.Dest` and overwrites `acc.Dest` in parallel when the most significant differing word matches `acc.TargetCmpBits`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorCompareAccumulateParallel(VectorAccumulateContext acc);
```

## ComputeBlockBitLength(BlockBitLengthContext, SharedMemory\<ulong\>)

Computes the exact bit length of a `ctx.NumWords`-word slice starting at `ctx.BaseOffset` in `ctx.Data` using a block-wide maximum reduction.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ComputeBlockBitLength(BlockBitLengthContext ctx, SharedMemory<ulong> sharedMax);
```

## DeviceVectorFloatCompareElement(ElementCompareContext)

Compares two floating-point elements (`cmp.Left` and `cmp.Right`) across a thread block, accounting for zero magnitudes, signs, normalized exponents ($e + \text{bitLen}$), and bit-shifted significands. Returns `1u` if left > right, `2u` if left < right, or `0u` if equal.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint DeviceVectorFloatCompareElement(ElementCompareContext cmp);
```

## EvaluateCompareOperator(uint, int)

Maps a 3-way comparison result `cmpRes` (`0`, `1`, or `2`) and an operator code `opType` (`0` = `==`, `1` = `!=`, `2` = `>`, `3` = `>=`, `4` = `<`, `5` = `<=`) to a boolean value.

```csharp
[GpuDeviceFunction]
[GpuInline]
public static bool EvaluateCompareOperator(uint cmpRes, int opType);
```

## VectorFloatCompareKernel(VectorFloatCompareContext)

Compares each element of a `GpuBigFloatArray` batch against a scalar (`ctx.IsScalar == true`) or a second `GpuBigFloatArray` batch and writes `1u` or `0u` predicate flags to `ctx.Result`.

```csharp
[GpuKernel]
public static void VectorFloatCompareKernel(VectorFloatCompareContext ctx);
```

## InitFloatReductionChunkElement(VectorFloatReductionChunkContext)

Initializes the block output slot in `ctx.Output` with the first element of the chunk assigned to `BlockIndex.X`, returning the starting element index (`startIdx`) or `-1` if the block starts beyond `ctx.BatchSize`.

```csharp
[GpuDeviceFunction]
[GpuInline]
public static int InitFloatReductionChunkElement(VectorFloatReductionChunkContext ctx);
```

## VectorFloatMaxReductionChunkKernel(VectorFloatReductionChunkContext)

Reduces chunks of a `GpuBigFloatArray` batch to their maximum value.

```csharp
[GpuKernel]
public static void VectorFloatMaxReductionChunkKernel(VectorFloatReductionChunkContext ctx);
```

## VectorFloatMinReductionChunkKernel(VectorFloatReductionChunkContext)

Reduces chunks of a `GpuBigFloatArray` batch to their minimum value.

```csharp
[GpuKernel]
public static void VectorFloatMinReductionChunkKernel(VectorFloatReductionChunkContext ctx);
```

## DeviceVectorFloatCompareReductionChunk(VectorFloatReductionChunkContext, uint)

Reduces a chunk of up to `ctx.ChunkSize` floating-point elements to their maximum (`targetCmpBits == 1u`) or minimum (`targetCmpBits == 2u`) using `DeviceVectorFloatCompareElement`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorFloatCompareReductionChunk(
    VectorFloatReductionChunkContext ctx,
    uint targetCmpBits);
```

## ComputeFloatAlignmentOffsets(ref FloatAlignmentInput)

Computes the radix-alignment bit shifts (`BitOffsetA`, `BitOffsetB`) and output exponent (`OutExp`) for two floating-point significands of bit lengths `BitLenA` and `BitLenB` and exponents `ExpA` and `ExpB`.

```csharp
[GpuDeviceFunction]
[GpuInline]
public static void ComputeFloatAlignmentOffsets(ref FloatAlignmentInput align);
```

## ShiftElementInPlaceBlockParallel(MutableElementSliceContext)

Shifts the `slice.NumWords`-word element at `slice.BaseOffset` in `slice.Buffer` by `slice.BitOffset` bits in place across the thread block.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void ShiftElementInPlaceBlockParallel(MutableElementSliceContext slice);
```

## NegateElementInPlaceBlockParallel(MutableElementSliceContext)

Performs an in-place two's-complement negation of the `slice.NumWords`-word element at `slice.BaseOffset` in `slice.Buffer` across the thread block.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void NegateElementInPlaceBlockParallel(MutableElementSliceContext slice);
```

## AccumulateFloatElementBlockParallel(FloatAccumulateStepContext)

Aligns and accumulates the floating-point element `step.Source` into the running block accumulator in `step.Dest`, `step.ExpResult`, and `step.SignResult`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void AccumulateFloatElementBlockParallel(FloatAccumulateStepContext step);
```

## VectorFloatSumReductionChunkKernel(VectorFloatReductionChunkContext)

Reduces chunks of a `GpuBigFloatArray` batch by summation.

```csharp
[GpuKernel]
public static void VectorFloatSumReductionChunkKernel(VectorFloatReductionChunkContext ctx);
```

## DeviceVectorFloatSumReductionChunk(VectorFloatReductionChunkContext)

Accumulates a chunk of up to `ctx.ChunkSize` floating-point elements into `ctx.Output` using `AccumulateFloatElementBlockParallel`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorFloatSumReductionChunk(VectorFloatReductionChunkContext ctx);
```

## VectorAnyTrueKernel(VectorBoolReductionContext)

Checks active predicate flags in `ctx.Flags` (up to `ResolveCount(ctx.Count)`) and sets `ctx.Result[0] = 1u` if any element is non-zero.

```csharp
[GpuKernel]
public static void VectorAnyTrueKernel(VectorBoolReductionContext ctx);
```

## VectorAllTrueKernel(VectorBoolReductionContext)

Checks active predicate flags in `ctx.Flags` (up to `ResolveCount(ctx.Count)`) and sets `ctx.Result[0] = 0u` if any element is zero.

```csharp
[GpuKernel]
public static void VectorAllTrueKernel(VectorBoolReductionContext ctx);
```

## WriteElementCountKernel(ElementCountWriteContext)

Resolves `ctx.Count` on the device and writes the resulting count into the first word of `ctx.Destination`, zeroing the remaining words up to `ctx.DestinationWords`.

```csharp
[GpuKernel]
public static void WriteElementCountKernel(ElementCountWriteContext ctx);
```

## CopyCountedElementsKernel(CountedCopyContext)

Copies the active elements (`ResolveCount(ctx.Count)`) of width `ctx.WordsPerElement` from `ctx.Source` (starting after `ctx.Count.Skip` elements) to `ctx.Destination`.

```csharp
[GpuKernel]
public static void CopyCountedElementsKernel(CountedCopyContext ctx);
```

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// Inspect the cached kernel handle fields on GpuBigIntVectorKernels.
IntPtr handle = GpuBigIntVectorKernels.VectorSignMagnitudeToTwosComplementKernelHandle;
Console.WriteLine($"{nameof(GpuBigIntVectorKernels)} handle initialized: {handle != IntPtr.Zero}");
```

## Remarks

Each block (`BlockIndex.X`) in `GpuBigIntVectorKernels` typically processes one multi-word integer or floating-point element in a batch (or one chunk of elements during `Sum`/`Min`/`Max` reductions), while threads within the block (`ThreadIndex.X`) cooperate across words using warp-level Kogge-Stone scans and shared memory.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [Arbitrary-Precision Floats (GpuBigFloat)](../../../../03_bcl_api/math_bignum/gpu_big_float/00_index.md)
- [GpuStreamCompactionKernels](GpuStreamCompactionKernels.md)
- [GpuBigIntVectorLinqExtensions](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/expressions/GpuBigIntVectorLinqExtensions.md)
- [GpuBigFloatVectorLinqExtensions](../../../../03_bcl_api/math_bignum/gpu_big_float/GpuBigFloatVectorLinqExtensions.md)
