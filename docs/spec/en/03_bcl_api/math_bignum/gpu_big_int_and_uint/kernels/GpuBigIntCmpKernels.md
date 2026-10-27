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
| [DecideSignedCompare(uint, uint, bool, uint, bool)](#decidesignedcompareuint-uint-bool-uint-bool) | A GPU device function (`[GpuDeviceFunction]`) that combines the signs of two integers with the comparison of their magnitudes. |
| [DeviceVectorCompareSignedParallel(VectorCompareContext)](#devicevectorcomparesignedparallelvectorcomparecontext) | A GPU device function (`[GpuDeviceFunction]`) that compares one element of a batched vector with a vector or a scalar as signed integers. |
| [GetBitLengthKernel(uint[], uint[], int)](#getbitlengthkerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that calculates the bit length of a large integer. |
| [IsOneKernel(uint[], uint[], int)](#isonekerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks if a large integer is equal to one. |
| [IsZeroKernel(uint[], uint[], int)](#iszerokerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks if a large integer is zero. |
| [ReadCompareSign(SignedCompareOperand, int)](#readcomparesignsignedcompareoperand-int) | A GPU device function (`[GpuDeviceFunction]`) that reads the sign of an element, or the sign of a scalar. |
| [ReadCompareWord(SignedCompareOperand, int, int)](#readcomparewordsignedcompareoperand-int-int) | A GPU device function (`[GpuDeviceFunction]`) that reads one word of an element, or 0 at or above the operand's word count. |
| [VectorAllTrueKernel(uint[], uint[], int)](#vectoralltruekerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks whether all elements in a boolean mask array are non-zero. |
| [VectorAndKernel(uint[], uint[], uint[], int)](#vectorandkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that computes the element-wise logical AND of two boolean mask arrays. |
| [VectorAnyTrueKernel(uint[], uint[], int)](#vectoranytruekerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks whether any element in a boolean mask array is non-zero. |
| [VectorCompareKernel(VectorCompareContext)](#vectorcomparekernelvectorcomparecontext) | A GPU kernel (`[GpuKernel]`) that compares a batched vector of signed big integers element-wise with another vector or a scalar. |
| [VectorNotKernel(uint[], uint[], int)](#vectornotkerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that computes the element-wise logical NOT of a boolean mask array. |
| [VectorOrKernel(uint[], uint[], uint[], int)](#vectororkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that computes the element-wise logical OR of two boolean mask arrays. |

## CompareToKernel(uint[], uint[], ulong[], int)

A GPU kernel (`[GpuKernel]`) that compares two large integers.

```csharp
[GpuKernel]
public static void CompareToKernel(uint[] dA, uint[] dB, ulong[] dResult, int numWords);
```

## DecideSignedCompare(uint, uint, bool, uint, bool)

A GPU device function (`[GpuDeviceFunction]`) that combines the signs of two integers with the comparison of their magnitudes `magCmp` (0 = equal, 1 = less, 2 = greater) and returns the signed comparison in the same encoding. A negative zero (sign set, magnitude 0) is treated as zero.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint DecideSignedCompare(uint magCmp, uint signA, bool nonzeroA, uint signB, bool nonzeroB);
```

## DeviceVectorCompareSignedParallel(VectorCompareContext)

A GPU device function (`[GpuDeviceFunction]`) that compares element `BlockIndex.X` of `ctx.Left` with `ctx.Right` as signed integers and writes 1 to `ctx.Result[BlockIndex.X]` when the operator `ctx.OpType` holds, otherwise 0. The two sides may have different word counts; the missing upper words of the narrower side are 0.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceVectorCompareSignedParallel(VectorCompareContext ctx);
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

## ReadCompareSign(SignedCompareOperand, int)

A GPU device function (`[GpuDeviceFunction]`) that returns the sign word of element `element`, or `ScalarSign` when the operand is a scalar (`Signs` is `null`).

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ReadCompareSign(SignedCompareOperand operand, int element);
```

## ReadCompareWord(SignedCompareOperand, int, int)

A GPU device function (`[GpuDeviceFunction]`) that returns word `idx` of element `element`, or 0 when `idx` is at or above the operand's `NumWords`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ReadCompareWord(SignedCompareOperand operand, int element, int idx);
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

## VectorCompareKernel(VectorCompareContext)

A GPU kernel (`[GpuKernel]`) that compares a batched vector of signed big integers element-wise with another vector or a scalar. Launch one block per element with a multiple of 32 threads (at most 1024), as many as the wider side's word count rounded up to whole warps, so that no warp is partial; a narrower block loops over the words, and lanes past the word count read nothing. An empty vector must not be launched (a grid of 0 blocks is invalid). Each side is read with its own word count and stride, so a scalar narrower than the elements is never read past its last word.

```csharp
[GpuKernel]
public static void VectorCompareKernel(VectorCompareContext ctx);

public struct SignedCompareOperand
{
    [GpuReadOnly] public uint[] Limbs;
    [GpuReadOnly] public uint[] Signs;   // null for a scalar
    public int NumWords;                 // words this side stores per element
    public int Stride;                   // words between elements; 0 for a scalar
    public uint ScalarSign;              // used when Signs is null
}

public struct VectorCompareContext
{
    public SignedCompareOperand Left;
    public SignedCompareOperand Right;
    public uint[] Result;
    public int OpType;                   // VectorCompareOperator
}
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
