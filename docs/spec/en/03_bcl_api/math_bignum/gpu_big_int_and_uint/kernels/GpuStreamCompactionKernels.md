---
title: "GpuStreamCompactionKernels Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for work-efficient parallel prefix sums (Blelloch exclusive scan) and stream compaction on GpuBigUIntArray."
---

## GpuStreamCompactionKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels for stream compaction (`Where` filtering), including the work-efficient parallel prefix sum (Blelloch exclusive scan) with zero-bank-conflict shared memory padding.

```csharp
public static partial class GpuStreamCompactionKernels
```

## Methods

| Name | Description |
| --- | --- |
| [BlockExclusiveScan(uint[], uint[], int, uint[])](#blockexclusivescanuint-uint-int-uint) | A GPU kernel (`[GpuKernel]`) that performs a block-wide exclusive prefix sum (Blelloch scan) on a `uint` array and optionally records block totals. |
| [AddBlockSums(uint[], uint[], int)](#addblocksumsuint-uint-int) | A GPU kernel (`[GpuKernel]`) that adds scanned block sums back into each element of the corresponding block. |
| [CompactBigUIntArray(uint[], uint[], uint[], uint[], int, int)](#compactbiguintarrayuint-uint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that scatters surviving multi-word elements to the compacted output buffer using predicate flags and exclusive scan indices. |
| [MemsetZero(uint[], int)](#memsetzerouint-int) | A GPU kernel (`[GpuKernel]`) that zeroes out a `uint` device buffer in parallel. |

## BlockExclusiveScan(uint[], uint[], int, uint[])

Performs a block-wide exclusive prefix sum (Blelloch scan) on a `uint` array using bank-conflict-free shared memory addressing.

```csharp
[GpuKernel]
public static void BlockExclusiveScan(uint[] output, uint[] input, int elementCount, uint[] blockSums);
```

#### Parameters

`output` _uint[]_<br>
The destination array for the exclusive scan result.

`input` _uint[]_<br>
The source array containing predicate flags (`1` or `0`) or unsigned integers.

`elementCount` _int_<br>
The number of valid elements to process.

`blockSums` _uint[]_<br>
An auxiliary array to store the total sum of each block for multi-block hierarchical scans.

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

`GpuStreamCompactionKernels` implements the classic three-phase work-efficient Blelloch scan (up-sweep reduce, down-sweep scan, and multi-block sum propagation) with conflict-free shared memory padding (`(n >> 5) + (n >> 10)`), powering zero-allocation GPU `Where` filtering on `GpuBigIntArray` and `GpuBigUIntArray`.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuBigIntVectorLinqExtensions](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/expressions/GpuBigIntVectorLinqExtensions.md)
