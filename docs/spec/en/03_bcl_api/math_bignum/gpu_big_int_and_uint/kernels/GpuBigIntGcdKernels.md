---
title: "GpuBigIntGcdKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Greatest Common Divisor (GCD) algorithms."
---

## GpuBigIntGcdKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Contains native G# (CUDA) kernels used for GCD computations, particularly Lehmer's GCD algorithm matrix reductions on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntGcdKernels
```

## Methods

| Name | Description |
| --- | --- |
| [ApplyMatrix2x2Kernel(uint[], uint[], uint[], uint[], long, long, long, long, int)](#applymatrix2x2kerneluint-uint-uint-uint-long-long-long-long-int) | A GPU kernel (`[GpuKernel]`) that applies a 2x2 reduction matrix to a pair of large integers. |

## ApplyMatrix2x2Kernel(uint[], uint[], uint[], uint[], long, long, long, long, int)

A GPU kernel (`[GpuKernel]`) that applies a 2x2 reduction matrix to a pair of large integers.

```csharp
[GpuKernel]
public static void ApplyMatrix2x2Kernel(uint[] a, uint[] b, uint[] dstA, uint[] dstB, long u0, long v0, long u1, long v1, int numWords);
```

#### Parameters

`a` _uint[]_<br>
The first operand buffer.

`b` _uint[]_<br>
The second operand buffer.

`dstA` _uint[]_<br>
The destination buffer for the first transformed operand.

`dstB` _uint[]_<br>
The destination buffer for the second transformed operand.

`u0` _long_<br>
The matrix element at row 0, column 0.

`v0` _long_<br>
The matrix element at row 0, column 1.

`u1` _long_<br>
The matrix element at row 1, column 0.

`v1` _long_<br>
The matrix element at row 1, column 1.

`numWords` _int_<br>
The total number of words.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntGcdKernels contains static kernel endpoints for GCD ops.");
```

## Remarks

This class contains `[GpuKernel]` and `[GpuDeviceFunction]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuWorkspace`.

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
- [Advanced: Extending GpuBigInteger with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
- [7. Stdlib Math and Lazy Evaluation](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
