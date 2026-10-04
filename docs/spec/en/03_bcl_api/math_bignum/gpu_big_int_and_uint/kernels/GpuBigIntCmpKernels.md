---
title: "GpuBigIntCmpKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for large integer comparisons."
---

## GpuBigIntCmpKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels used for equality checks, comparisons, and bit-length measurements of arbitrary-precision integers on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntCmpKernels
```

## Methods

| Name | Description |
| --- | --- |
| [IsZeroKernel(uint[], uint[], int)](#iszerokerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks if a large integer is zero. |
| [IsOneKernel(uint[], uint[], int)](#isonekerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that checks if a large integer is equal to one. |
| [CompareToKernel(uint[], uint[], ulong[], int)](#comparetokerneluint-uint-ulong-int) | A GPU kernel (`[GpuKernel]`) that compares two large integers. |
| [GetBitLengthKernel(uint[], uint[], int)](#getbitlengthkerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that calculates the bit length of a large integer. |

## IsZeroKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that checks if a large integer is zero.

```csharp
[GpuKernel]
public static void IsZeroKernel(uint[] dA, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The large integer operand buffer.

`dResult` _uint[]_<br>
The output buffer (single element) to store the result flag. Set to 1 if any non-zero word is found.

`numWords` _int_<br>
The total number of words.

## IsOneKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that checks if a large integer is equal to one.

```csharp
[GpuKernel]
public static void IsOneKernel(uint[] dA, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The large integer operand buffer.

`dResult` _uint[]_<br>
The output buffer (single element) to store the result flag. Set to 1 if the value is not exactly one.

`numWords` _int_<br>
The total number of words.

## CompareToKernel(uint[], uint[], ulong[], int)

A GPU kernel (`[GpuKernel]`) that compares two large integers.

```csharp
[GpuKernel]
public static void CompareToKernel(uint[] dA, uint[] dB, ulong[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The first operand buffer.

`dB` _uint[]_<br>
The second operand buffer.

`dResult` _ulong[]_<br>
The output buffer storing the highest differing word index and the comparison result.

`numWords` _int_<br>
The total number of words.

## GetBitLengthKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that calculates the bit length of a large integer.

```csharp
[GpuKernel]
public static void GetBitLengthKernel(uint[] dA, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The large integer operand buffer.

`dResult` _uint[]_<br>
The output buffer storing the maximum calculated bit length.

`numWords` _int_<br>
The total number of words.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntCmpKernels contains static kernel endpoints for comparison ops.");
```

## Remarks

This class contains `[GpuKernel]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuWorkspace`.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### BCL API > GPU Class

- [Gpu Class](../../../../03_bcl_api/gpu_class/00_index.md)
- [InlinePtx](../../../../03_bcl_api/gpu_class/InlinePtx.md)

### Guides

- [4. Practical Guides and Best Practices](../../../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
- [7. Stdlib Math and Lazy Evaluation](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
