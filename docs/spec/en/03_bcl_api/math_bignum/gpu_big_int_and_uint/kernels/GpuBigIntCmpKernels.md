---
title: "GpuBigIntCmpKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for large integer comparisons and vectorized boolean predicates."
---

## GpuBigIntCmpKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and device functions used for equality checks, comparisons, bit-length measurements, and batched vector comparisons/boolean reductions of arbitrary-precision integers on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigIntCmpKernels
```

## Methods

| Name | Description |
| --- | --- |
| [CompareToKernel(uint[], uint[], ulong[], int)](#comparetokerneluint-uint-ulong-int) | A GPU kernel (`[GpuKernel]`) that compares two large integers. |
| [DeviceVectorCompareScalarParallel(uint[], uint[], uint[], int, int)](#devicevectorcomparescalarparalleluint-uint-uint-int-int) | A GPU device function (`[GpuDeviceFunction]`) that compares each element of a batched vector against a scalar big integer in parallel. |
| [DeviceVectorCompareVectorParallel(uint[], uint[], uint[], int, int)](#devicevectorcomparevectorparalleluint-uint-uint-int-int) | A GPU device function (`[GpuDeviceFunction]`) that compares corresponding elements of two batched vectors in parallel. |
| [GetBitLengthKernel(uint[], uint[], int)](#getbitlengthkerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that calculates the bit length of a large integer. |
| [IsOneKernel(uint[], uint[], int)](#isonekerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks if a large integer is equal to one. |
| [IsZeroKernel(uint[], uint[], int)](#iszerokerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks if a large integer is zero. |
| [LeadingBitsKernel(uint[], uint[], int)](#leadingbitskerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that writes the leading 64 bits of a large integer, given its bit length. |
| [VectorAllTrueKernel(uint[], uint[], int)](#vectoralltruekerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks whether all elements in a boolean mask array are non-zero. |
| [VectorAndKernel(uint[], uint[], uint[], int)](#vectorandkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that computes the element-wise logical AND of two boolean mask arrays. |
| [VectorAnyTrueKernel(uint[], uint[], int)](#vectoranytruekerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks whether any element in a boolean mask array is non-zero. |
| [VectorCompareScalarKernel(uint[], uint[], uint[], int, int)](#vectorcomparescalarkerneluint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that compares a batched vector of big integers against a scalar big integer. |
| [VectorCompareVectorKernel(uint[], uint[], uint[], int, int)](#vectorcomparevectorkerneluint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that compares two batched vectors of big integers element-wise. |
| [VectorNotKernel(uint[], uint[], int)](#vectornotkerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that computes the element-wise logical NOT of a boolean mask array. |
| [VectorOrKernel(uint[], uint[], uint[], int)](#vectororkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that computes the element-wise logical OR of two boolean mask arrays. |

## CompareToKernel(uint[], uint[], ulong[], int)

A GPU kernel (`[GpuKernel]`) that compares two large integers.

```csharp
[GpuKernel]
public static void CompareToKernel(uint[] dA, uint[] dB, ulong[] dResult, int numWords);
```

## DeviceVectorCompareScalarParallel(uint[], uint[], uint[], int, int)

A GPU device function (`[GpuDeviceFunction]`) that compares each element of a batched vector against a scalar big integer in parallel.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorCompareScalarParallel(
    [GpuReadOnly] uint[] a,
    [GpuReadOnly] uint[] s,
    uint[] result,
    int numWords,
    int opType);
```

## DeviceVectorCompareVectorParallel(uint[], uint[], uint[], int, int)

A GPU device function (`[GpuDeviceFunction]`) that compares corresponding elements of two batched vectors in parallel.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorCompareVectorParallel(
    [GpuReadOnly] uint[] a,
    [GpuReadOnly] uint[] b,
    uint[] result,
    int numWords,
    int opType);
```

## GetBitLengthKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that calculates the bit length of a large integer.

```csharp
[GpuKernel]
public static void GetBitLengthKernel(uint[] dA, uint[] dResult, int numWords);
```

## IsOneKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that checks if a large integer is equal to one.

```csharp
[GpuKernel]
public static void IsOneKernel(uint[] dA, uint[] dResult, int numWords);
```

## IsZeroKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that checks if a large integer is zero.

```csharp
[GpuKernel]
public static void IsZeroKernel(uint[] dA, uint[] dResult, int numWords);
```

## LeadingBitsKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that writes the leading 64 bits of a large integer to `dResult[1]` (low word) and `dResult[2]` (high word), given its bit length in `dResult[0]` from `GetBitLengthKernel` on the same stream. The top set bit lands at bit 63; missing low bits are `0`.

```csharp
[GpuKernel]
public static void LeadingBitsKernel(uint[] dA, uint[] dResult, int numWords);
```

## VectorAllTrueKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that checks whether all elements in a boolean mask array are non-zero.

```csharp
[GpuKernel]
public static void VectorAllTrueKernel(uint[] dA, uint[] dResult, int length);
```

## VectorAndKernel(uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that computes the element-wise logical AND of two boolean mask arrays.

```csharp
[GpuKernel]
public static void VectorAndKernel(uint[] dA, uint[] dB, uint[] dResult, int length);
```

## VectorAnyTrueKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that checks whether any element in a boolean mask array is non-zero.

```csharp
[GpuKernel]
public static void VectorAnyTrueKernel(uint[] dA, uint[] dResult, int length);
```

## VectorCompareScalarKernel(uint[], uint[], uint[], int, int)

A GPU kernel (`[GpuKernel]`) that compares a batched vector of big integers against a scalar big integer.

```csharp
[GpuKernel]
public static void VectorCompareScalarKernel(uint[] dA, uint[] s, uint[] dResult, int numWords, int opType);
```

## VectorCompareVectorKernel(uint[], uint[], uint[], int, int)

A GPU kernel (`[GpuKernel]`) that compares two batched vectors of big integers element-wise.

```csharp
[GpuKernel]
public static void VectorCompareVectorKernel(uint[] dA, uint[] dB, uint[] dResult, int numWords, int opType);
```

## VectorNotKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that computes the element-wise logical NOT of a boolean mask array.

```csharp
[GpuKernel]
public static void VectorNotKernel(uint[] dA, uint[] dResult, int length);
```

## VectorOrKernel(uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that computes the element-wise logical OR of two boolean mask arrays.

```csharp
[GpuKernel]
public static void VectorOrKernel(uint[] dA, uint[] dB, uint[] dResult, int length);
```

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// This is a GPU kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine($"Kernel container: {typeof(GpuBigIntCmpKernels).Name}");
```

## Remarks

This class contains `[GpuKernel]` and `[GpuDeviceFunction]` attributed methods. These methods are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuWorkspace`.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
