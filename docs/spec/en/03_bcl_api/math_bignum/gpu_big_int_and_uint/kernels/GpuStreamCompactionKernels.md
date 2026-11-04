---
title: "GpuStreamCompactionKernels Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for work-efficient parallel prefix sums (Blelloch exclusive scan) and stream compaction on GpuBigUIntArray, GpuBigIntArray, and GpuBigFloatArray."
---

## GpuStreamCompactionKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and device functions for stream compaction (`Where` filtering), including the work-efficient parallel prefix sum (Blelloch exclusive scan) with zero-bank-conflict shared memory padding, device-side count resolution, and multi-buffer scatter for unsigned, signed, and floating-point vectors.

```csharp
[SkipSnapshotConsistency("Tested in separate suites")]
[ExcludeFromCodeCoverage]
public static partial class GpuStreamCompactionKernels
```

## Methods

| Name | Description |
| --- | --- |
| [ResolveCount(GpuBigIntVectorKernels.ElementCountContext)](#resolvecountgpubigintvectorkernelselementcountcontext) | A GPU device function (`[GpuDeviceFunction]`) that resolves the active element count from a device-resident count buffer (`Base`, `Skip`, and `Limit`). |
| [BlockExclusiveScan(uint[], uint[], int, uint[]?, GpuBigIntVectorKernels.ElementCountContext, uint[]?, bool)](#blockexclusivescanuint-uint-int-uint-gpubigintvectorkernelselementcountcontext-uint-bool) | A GPU kernel (`[GpuKernel]`) that performs a block-wide exclusive prefix sum (Blelloch scan) on a `uint` array and optionally writes block sums and the total count. |
| [AddBlockSums(uint[], uint[], int)](#addblocksumsuint-uint-int) | A GPU kernel (`[GpuKernel]`) that adds scanned block sums back into each element of the corresponding block. |
| [CompactBigUIntArray(uint[], uint[], uint[], uint[], int, int)](#compactbiguintarrayuint-uint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that scatters surviving multi-word unsigned elements to the compacted output buffer using predicate flags and exclusive scan indices. |
| [CompactSignedVector(uint[], uint[], uint[], uint[], uint[], uint[], int, int, GpuBigIntVectorKernels.ElementCountContext)](#compactsignedvectoruint-uint-uint-uint-uint-uint-int-int-gpubigintvectorkernelselementcountcontext) | A GPU kernel (`[GpuKernel]`) that scatters surviving signed multi-word elements (values and signs) to the compacted output buffers, respecting a device-resident element count when present. |
| [CompactFloatVector(uint[], uint[], uint[], uint[], uint[], uint[], uint[], uint[], int, int, GpuBigIntVectorKernels.ElementCountContext)](#compactfloatvectoruint-uint-uint-uint-uint-uint-uint-uint-int-int-gpubigintvectorkernelselementcountcontext) | A GPU kernel (`[GpuKernel]`) that scatters surviving floating-point elements (significands, exponents, and signs) to the compacted output buffers, respecting a device-resident element count when present. |
| [MemsetZero(uint[], int)](#memsetzerouint-int) | A GPU kernel (`[GpuKernel]`) that zeroes out a `uint` device buffer in parallel. |

## ResolveCount(GpuBigIntVectorKernels.ElementCountContext)

Resolves the number of active elements on the device from `count.Base[0]`, subtracting `count.Skip` (clamped at zero) and clamping the result to `count.Limit` when `count.Limit >= 0`.

```csharp
[GpuDeviceFunction]
[GpuInline]
public static int ResolveCount(GpuBigIntVectorKernels.ElementCountContext count);
```

#### Parameters

`count` _[GpuBigIntVectorKernels.ElementCountContext](GpuBigIntVectorKernels.md#nested-context-structs)_<br>
The device-resident element count context (`Base`, `Skip`, and `Limit`).

#### Returns

_int_<br>
The active element count on the device.

## BlockExclusiveScan(uint[], uint[], int, uint[]?, GpuBigIntVectorKernels.ElementCountContext, uint[]?, bool)

Performs a block-wide exclusive prefix sum (Blelloch scan) on a `uint` array using bank-conflict-free shared memory addressing. When `flags` is `true` and `count.Base` is non-null, elements at or beyond `ResolveCount(count)` are treated as zero; in a single-block scan, the last thread also writes the total sum into `total[0]` when `total` is non-null.

```csharp
[GpuKernel]
public static void BlockExclusiveScan(
    uint[] output,
    uint[] input,
    int elementCount,
    uint[]? blockSums,
    GpuBigIntVectorKernels.ElementCountContext count,
    uint[]? total,
    bool flags);
```

#### Parameters

`output` _uint[]_<br>
The destination array for the exclusive scan result.

`input` _uint[]_<br>
The source array containing predicate flags (`1` or `0`) or block totals.

`elementCount` _int_<br>
The upper bound on the number of elements to scan.

`blockSums` _uint[]?_<br>
An optional auxiliary array to store the total sum of each block for multi-block hierarchical scans.

`count` _[GpuBigIntVectorKernels.ElementCountContext](GpuBigIntVectorKernels.md#nested-context-structs)_<br>
The optional device-resident element count context used when `flags` is `true`.

`total` _uint[]?_<br>
An optional 1-word device buffer receiving the total count of selected elements when `blockSums` is `null` (single-block scan) or on the final hierarchical level.

`flags` _bool_<br>
`true` when `input` holds per-element `0`/`1` predicate flags that should be masked by `count`; `false` when scanning an array of block sums.

## AddBlockSums(uint[], uint[], int)

Adds the scanned block sums to all elements in the corresponding block (Phase 3 of the multi-block arbitrary-size scan).

```csharp
[GpuKernel]
public static void AddBlockSums(uint[] output, uint[] blockSums, int elementCount);
```

#### Parameters

`output` _uint[]_<br>
The block-scanned output array to update in place.

`blockSums` _uint[]_<br>
The prefix-scanned array of per-block totals.

`elementCount` _int_<br>
The total number of valid elements.

## CompactBigUIntArray(uint[], uint[], uint[], uint[], int, int)

Scatters the surviving multi-word `GpuBigUInt` elements to the output array based on predicate flags and their exclusive prefix sums.

```csharp
[GpuKernel]
public static void CompactBigUIntArray(
    uint[] output,
    uint[] input,
    uint[] predicates,
    uint[] scanResult,
    int elementCount,
    int wordsPerElement);
```

#### Parameters

`output` _uint[]_<br>
The destination compacted multi-word buffer.

`input` _uint[]_<br>
The source contiguous multi-word buffer.

`predicates` _uint[]_<br>
The boolean predicate array (`1` to keep, `0` to discard).

`scanResult` _uint[]_<br>
The exclusive prefix sum of `predicates`, providing the destination index for each kept element.

`elementCount` _int_<br>
The number of elements in the input batch.

`wordsPerElement` _int_<br>
The number of 32-bit words per element.

## CompactSignedVector(uint[], uint[], uint[], uint[], uint[], uint[], int, int, GpuBigIntVectorKernels.ElementCountContext)

Scatters the surviving signed multi-word elements (`inputValues` and `inputSigns`) to `outputValues` and `outputSigns` based on `predicates` and `scanResult`, clamping the active range to `ResolveCount(count)` when `count.Base` is non-null.

```csharp
[GpuKernel]
public static void CompactSignedVector(
    uint[] outputValues,
    uint[] outputSigns,
    uint[] inputValues,
    uint[] inputSigns,
    uint[] predicates,
    uint[] scanResult,
    int elementCount,
    int wordsPerElement,
    GpuBigIntVectorKernels.ElementCountContext count);
```

#### Parameters

`outputValues` _uint[]_<br>
The destination compacted multi-word magnitude buffer.

`outputSigns` _uint[]_<br>
The destination compacted sign buffer (1 word per element).

`inputValues` _uint[]_<br>
The source multi-word magnitude buffer.

`inputSigns` _uint[]_<br>
The source sign buffer (1 word per element).

`predicates` _uint[]_<br>
The boolean predicate array (`1` to keep, `0` to discard).

`scanResult` _uint[]_<br>
The exclusive prefix sum of `predicates`.

`elementCount` _int_<br>
The capacity (maximum element count) of the input batch.

`wordsPerElement` _int_<br>
The number of 32-bit magnitude words per element.

`count` _[GpuBigIntVectorKernels.ElementCountContext](GpuBigIntVectorKernels.md#nested-context-structs)_<br>
The optional device-resident element count context when compacting an already-filtered vector.

## CompactFloatVector(uint[], uint[], uint[], uint[], uint[], uint[], uint[], uint[], int, int, GpuBigIntVectorKernels.ElementCountContext)

Scatters the surviving floating-point elements (`inputSignificands`, `inputExponents`, and `inputSigns`) to `outputSignificands`, `outputExponents`, and `outputSigns` based on `predicates` and `scanResult`, clamping the active range to `ResolveCount(count)` when `count.Base` is non-null.

```csharp
[GpuKernel]
public static void CompactFloatVector(
    uint[] outputSignificands,
    uint[] outputExponents,
    uint[] outputSigns,
    uint[] inputSignificands,
    uint[] inputExponents,
    uint[] inputSigns,
    uint[] predicates,
    uint[] scanResult,
    int elementCount,
    int wordsPerElement,
    GpuBigIntVectorKernels.ElementCountContext count);
```

#### Parameters

`outputSignificands` _uint[]_<br>
The destination compacted multi-word significand buffer.

`outputExponents` _uint[]_<br>
The destination compacted exponent buffer (1 word per element).

`outputSigns` _uint[]_<br>
The destination compacted sign buffer (1 word per element).

`inputSignificands` _uint[]_<br>
The source multi-word significand buffer.

`inputExponents` _uint[]_<br>
The source exponent buffer (1 word per element).

`inputSigns` _uint[]_<br>
The source sign buffer (1 word per element).

`predicates` _uint[]_<br>
The boolean predicate array (`1` to keep, `0` to discard).

`scanResult` _uint[]_<br>
The exclusive prefix sum of `predicates`.

`elementCount` _int_<br>
The capacity (maximum element count) of the input batch.

`wordsPerElement` _int_<br>
The number of 32-bit significand words per element.

`count` _[GpuBigIntVectorKernels.ElementCountContext](GpuBigIntVectorKernels.md#nested-context-structs)_<br>
The optional device-resident element count context when compacting an already-filtered vector.

## MemsetZero(uint[], int)

Zeroes out a `uint` device buffer in parallel.

```csharp
[GpuKernel]
public static void MemsetZero(uint[] output, int totalWords);
```

#### Parameters

`output` _uint[]_<br>
The target device buffer to clear.

`totalWords` _int_<br>
The total number of 32-bit words to set to zero.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
Console.WriteLine($"{nameof(GpuStreamCompactionKernels)} provides Blelloch exclusive scan and stream compaction kernels.");
```

## Remarks

`GpuStreamCompactionKernels` implements the three-phase work-efficient Blelloch scan (up-sweep reduce, down-sweep scan, and multi-block sum propagation) with conflict-free shared memory padding (`(n >> 5) + (n >> 10)`), powering zero-allocation GPU `Where` filtering on `GpuBigUIntArray`, `GpuBigIntArray`, and `GpuBigFloatArray` without synchronizing the host to read intermediate element counts.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuBigIntVectorKernels](GpuBigIntVectorKernels.md)
- [GpuBigIntVectorLinqExtensions](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/expressions/GpuBigIntVectorLinqExtensions.md)
- [GpuBigFloatVectorLinqExtensions](../../../../03_bcl_api/math_bignum/gpu_big_float/GpuBigFloatVectorLinqExtensions.md)
