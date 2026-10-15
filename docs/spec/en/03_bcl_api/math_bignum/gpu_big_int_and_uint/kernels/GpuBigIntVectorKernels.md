---
title: "GpuBigIntVectorKernels Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for batched vector arithmetic, representation conversions, and parallel reductions (Sum, Min, Max) on GpuBigIntArray."
---

## GpuBigIntVectorKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and device functions for batched vector operations over `GpuBigIntArray` and `GpuBigUIntArray`, including sign-magnitude conversions, element-wise/scalar vector addition, subtraction, multiplication, and chunked `Sum`, `Min`, and `Max` reductions.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntVectorKernels
```

## Fields

| Name | Description |
| --- | --- |
| `VectorSignMagnitudeToTwosComplementKernelHandle` | Cached native CUDA kernel handle (`IntPtr`) for `VectorSignMagnitudeToTwosComplementKernel`. |
| `VectorSignMagnitudeToOffsetBinaryKernelHandle` | Cached native CUDA kernel handle (`IntPtr`) for `VectorSignMagnitudeToOffsetBinaryKernel`. |

## Methods

| Name | Description |
| --- | --- |
| [DeviceVectorSignMagnitudeToTwosComplement(uint[], uint[], uint[], int, int)](#devicevectorsignmagnitudetotwoscomplementuint-uint-uint-int-int) | A GPU device function (`[GpuDeviceFunction]`) that converts a batch of sign-magnitude multi-word integers into two's complement representation. |
| [VectorSignMagnitudeToTwosComplementKernel(uint[], uint[], uint[], int, int)](#vectorsignmagnitudetotwoscomplementkerneluint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that converts a batch of sign-magnitude integers into two's complement form. |
| [VectorSignMagnitudeToOffsetBinaryKernel(uint[], uint[], uint[], int, int)](#vectorsignmagnitudetooffsetbinarykerneluint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that converts a batch of sign-magnitude integers into offset-binary form (flipping the MSB of two's complement). |
| [VectorAddScalarKernel(uint[], uint[], uint[], uint[], uint, uint[], int, int, int)](#vectoraddscalarkerneluint-uint-uint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that adds a multi-word scalar to each element of a signed vector batch. |
| [VectorSubScalarKernel(uint[], uint[], uint[], uint[], uint, uint[], int, int, int)](#vectorsubscalarkerneluint-uint-uint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that subtracts a multi-word scalar from each element of a signed vector batch. |
| [VectorZipAddKernel(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int)](#vectorzipaddkerneluint-uint-uint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that performs element-wise addition of two signed vector batches. |
| [VectorZipSubKernel(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int)](#vectorzipsubkerneluint-uint-uint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that performs element-wise subtraction of two signed vector batches. |
| [DeviceVectorAddSubParallel(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int, uint, uint, bool, bool)](#devicevectoraddsubparalleluint-uint-uint-uint-uint-uint-int-int-int-uint-uint-bool-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs signed batched addition or subtraction with automatic two's complement negation when signs differ. |
| [VectorMulScalarKernel(uint[], uint[], uint[], uint[], uint, uint[], int, int, int)](#vectormulscalarkerneluint-uint-uint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that multiplies each element of a signed vector batch by a multi-word scalar. |
| [VectorZipMulKernel(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int)](#vectorzipmulkerneluint-uint-uint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that performs element-wise multiplication of two signed vector batches. |
| [DeviceVectorMulVectorParallel(uint[], uint[], uint[], int, int, int, bool, bool)](#devicevectormulvectorparalleluint-uint-uint-int-int-int-bool-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel multi-word multiplication for vector-scalar or vector-vector batches. |
| [DeviceVectorMulScalarParallel(uint[], uint[], uint[], int, bool)](#devicevectormulscalarparalleluint-uint-uint-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel same-width scalar multiplication across a vector batch. |
| [DeviceVectorAddSubVectorParallel(uint[], uint[], uint[], int, int, int, int, bool)](#devicevectoraddsubvectorparalleluint-uint-uint-int-int-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs unsigned batched element-wise addition or subtraction. |
| [VectorSumReductionChunkKernel(uint[], uint[], int, int, int)](#vectorsumreductionchunkkerneluint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch by summation. |
| [DeviceVectorSumReductionChunk(uint[], uint[], int, int, int)](#devicevectorsumreductionchunkuint-uint-int-int-int) | A GPU device function (`[GpuDeviceFunction]`) that accumulates a chunk of multi-word integers into a block-level sum buffer. |
| [DeviceVectorAddAccumulateParallel(uint[], int, uint[], int, int)](#devicevectoraddaccumulateparalleluint-int-uint-int-int) | A GPU device function (`[GpuDeviceFunction]`) that adds a multi-word integer slice into a destination slice using Kogge-Stone carry lookahead. |
| [VectorMaxReductionChunkKernel(uint[], uint[], int, int, int)](#vectormaxreductionchunkkerneluint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch to their maximum value. |
| [DeviceVectorMaxReductionChunk(uint[], uint[], int, int, int)](#devicevectormaxreductionchunkuint-uint-int-int-int) | A GPU device function (`[GpuDeviceFunction]`) that computes the maximum multi-word integer across a chunk. |
| [VectorMinReductionChunkKernel(uint[], uint[], int, int, int)](#vectorminreductionchunkkerneluint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch to their minimum value. |
| [DeviceVectorMinReductionChunk(uint[], uint[], int, int, int)](#devicevectorminreductionchunkuint-uint-int-int-int) | A GPU device function (`[GpuDeviceFunction]`) that computes the minimum multi-word integer across a chunk. |
| [DeviceVectorMaxAccumulateParallel(uint[], int, uint[], int, int)](#devicevectormaxaccumulateparalleluint-int-uint-int-int) | A GPU device function (`[GpuDeviceFunction]`) that updates the destination slice in place with the maximum of `src` and `dest`. |
| [DeviceVectorMinAccumulateParallel(uint[], int, uint[], int, int)](#devicevectorminaccumulateparalleluint-int-uint-int-int) | A GPU device function (`[GpuDeviceFunction]`) that updates the destination slice in place with the minimum of `src` and `dest`. |

## DeviceVectorSignMagnitudeToTwosComplement(uint[], uint[], uint[], int, int)

Converts a batch of sign-magnitude multi-word integers into two's complement representation.

```csharp
[GpuDeviceFunction]
public static void DeviceVectorSignMagnitudeToTwosComplement(
    [GpuReadOnly] uint[] src,
    [GpuReadOnly] uint[] signSrc,
    uint[] dest,
    int numWords,
    int batchSize);
```

## VectorSignMagnitudeToTwosComplementKernel(uint[], uint[], uint[], int, int)

A GPU kernel (`[GpuKernel]`) that converts a batch of sign-magnitude integers into two's complement form.

```csharp
[GpuKernel]
public static void VectorSignMagnitudeToTwosComplementKernel(
    [GpuReadOnly] uint[] src,
    [GpuReadOnly] uint[] signSrc,
    uint[] dest,
    int numWords,
    int batchSize);
```

## VectorSignMagnitudeToOffsetBinaryKernel(uint[], uint[], uint[], int, int)

A GPU kernel (`[GpuKernel]`) that converts a batch of sign-magnitude integers into offset-binary form (flipping the MSB of two's complement).

```csharp
[GpuKernel]
public static void VectorSignMagnitudeToOffsetBinaryKernel(
    [GpuReadOnly] uint[] src,
    [GpuReadOnly] uint[] signSrc,
    uint[] dest,
    int numWords,
    int batchSize);
```

## VectorAddScalarKernel(uint[], uint[], uint[], uint[], uint, uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that adds a multi-word scalar to each element of a signed vector batch.

```csharp
[GpuKernel]
public static void VectorAddScalarKernel(
    uint[] dA, uint[] s, uint[] dResult,
    uint[] signA, uint scalarSign, uint[] signResult,
    int numWordsA, int numWordsS, int numWordsResult);
```

## VectorSubScalarKernel(uint[], uint[], uint[], uint[], uint, uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that subtracts a multi-word scalar from each element of a signed vector batch.

```csharp
[GpuKernel]
public static void VectorSubScalarKernel(
    uint[] dA, uint[] s, uint[] dResult,
    uint[] signA, uint scalarSign, uint[] signResult,
    int numWordsA, int numWordsS, int numWordsResult);
```

## VectorZipAddKernel(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that performs element-wise addition of two signed vector batches.

```csharp
[GpuKernel]
public static void VectorZipAddKernel(
    uint[] dA, uint[] dB, uint[] dResult,
    uint[] signA, uint[] signB, uint[] signResult,
    int numWordsA, int numWordsB, int numWordsResult);
```

## VectorZipSubKernel(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that performs element-wise subtraction of two signed vector batches.

```csharp
[GpuKernel]
public static void VectorZipSubKernel(
    uint[] dA, uint[] dB, uint[] dResult,
    uint[] signA, uint[] signB, uint[] signResult,
    int numWordsA, int numWordsB, int numWordsResult);
```

## DeviceVectorAddSubParallel(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int, uint, uint, bool, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs signed batched addition or subtraction with automatic two's complement negation when signs differ.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorAddSubParallel(
    [GpuReadOnly] uint[] a,
    [GpuReadOnly] uint[] s,
    uint[] result,
    [GpuReadOnly] uint[] signA,
    [GpuReadOnly] uint[] signB,
    uint[] signResult,
    int numWordsA,
    int numWordsS,
    int numWordsResult,
    uint opSign,
    uint scalarSign,
    bool isScalar,
    bool syncThreads = true);
```

## VectorMulScalarKernel(uint[], uint[], uint[], uint[], uint, uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that multiplies each element of a signed vector batch by a multi-word scalar.

```csharp
[GpuKernel]
public static void VectorMulScalarKernel(
    uint[] dA, uint[] s, uint[] dResult,
    uint[] signA, uint scalarSign, uint[] signResult,
    int numWordsA, int numWordsS, int numWordsResult);
```

## VectorZipMulKernel(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that performs element-wise multiplication of two signed vector batches.

```csharp
[GpuKernel]
public static void VectorZipMulKernel(
    uint[] dA, uint[] dB, uint[] dResult,
    uint[] signA, uint[] signB, uint[] signResult,
    int numWordsA, int numWordsB, int numWordsResult);
```

## DeviceVectorMulVectorParallel(uint[], uint[], uint[], int, int, int, bool, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel multi-word multiplication for vector-scalar or vector-vector batches.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMulVectorParallel(
    [GpuReadOnly] uint[] a,
    [GpuReadOnly] uint[] b,
    uint[] result,
    int numWordsA,
    int numWordsB,
    int numWordsResult,
    bool isScalar,
    bool syncThreads = true);
```

## DeviceVectorMulScalarParallel(uint[], uint[], uint[], int, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel same-width scalar multiplication across a vector batch.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMulScalarParallel(
    [GpuReadOnly] uint[] a,
    [GpuReadOnly] uint[] s,
    uint[] result,
    int numWords,
    bool syncThreads = true);
```

## DeviceVectorAddSubVectorParallel(uint[], uint[], uint[], int, int, int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs unsigned batched element-wise addition or subtraction.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorAddSubVectorParallel(
    [GpuReadOnly] uint[] a,
    [GpuReadOnly] uint[] b,
    uint[] result,
    int numWordsA,
    int numWordsB,
    int numWordsResult,
    int sign,
    bool syncThreads = true);
```

## VectorSumReductionChunkKernel(uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch by summation.

```csharp
[GpuKernel]
public static void VectorSumReductionChunkKernel(uint[] dA, uint[] dResult, int numWords, int batchSize, int chunkSize);
```

## DeviceVectorSumReductionChunk(uint[], uint[], int, int, int)

A GPU device function (`[GpuDeviceFunction]`) that accumulates a chunk of multi-word integers into a block-level sum buffer.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorSumReductionChunk(
    [GpuReadOnly] uint[] a,
    uint[] result,
    int numWords,
    int batchSize,
    int chunkSize);
```

## DeviceVectorAddAccumulateParallel(uint[], int, uint[], int, int)

A GPU device function (`[GpuDeviceFunction]`) that adds a multi-word integer slice into a destination slice using Kogge-Stone carry lookahead.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorAddAccumulateParallel(
    [GpuReadOnly] uint[] src,
    int srcOffset,
    uint[] dest,
    int destOffset,
    int numWords);
```

## VectorMaxReductionChunkKernel(uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch to their maximum value.

```csharp
[GpuKernel]
public static void VectorMaxReductionChunkKernel(uint[] dA, uint[] dResult, int numWords, int batchSize, int chunkSize);
```

## DeviceVectorMaxReductionChunk(uint[], uint[], int, int, int)

A GPU device function (`[GpuDeviceFunction]`) that computes the maximum multi-word integer across a chunk.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMaxReductionChunk(
    [GpuReadOnly] uint[] a,
    uint[] result,
    int numWords,
    int batchSize,
    int chunkSize);
```

## VectorMinReductionChunkKernel(uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that reduces chunks of a multi-word integer batch to their minimum value.

```csharp
[GpuKernel]
public static void VectorMinReductionChunkKernel(uint[] dA, uint[] dResult, int numWords, int batchSize, int chunkSize);
```

## DeviceVectorMinReductionChunk(uint[], uint[], int, int, int)

A GPU device function (`[GpuDeviceFunction]`) that computes the minimum multi-word integer across a chunk.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMinReductionChunk(
    [GpuReadOnly] uint[] a,
    uint[] result,
    int numWords,
    int batchSize,
    int chunkSize);
```

## DeviceVectorMaxAccumulateParallel(uint[], int, uint[], int, int)

A GPU device function (`[GpuDeviceFunction]`) that updates the destination slice in place with the maximum of `src` and `dest`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMaxAccumulateParallel([GpuReadOnly] uint[] src, int srcOffset, uint[] dest, int destOffset, int numWords);
```

## DeviceVectorMinAccumulateParallel(uint[], int, uint[], int, int)

A GPU device function (`[GpuDeviceFunction]`) that updates the destination slice in place with the minimum of `src` and `dest`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorMinAccumulateParallel([GpuReadOnly] uint[] src, int srcOffset, uint[] dest, int destOffset, int numWords);
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

Each block (`BlockIndex.X`) in `GpuBigIntVectorKernels` typically processes one multi-word integer in a batch (or one chunk of elements during `Sum`/`Min`/`Max` reductions), while threads within the block (`ThreadIndex.X`) cooperate across words using warp-level Kogge-Stone scans and shared memory.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuBigIntVectorLinqExtensions](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/expressions/GpuBigIntVectorLinqExtensions.md)
