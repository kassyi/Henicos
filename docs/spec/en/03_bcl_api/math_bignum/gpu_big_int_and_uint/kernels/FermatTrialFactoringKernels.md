---
title: "FermatTrialFactoringKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Fermat number trial division factoring."
---

## FermatTrialFactoringKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Contains native G# (CUDA) kernels used for trial division factoring searches of Fermat numbers.

```csharp
[SkipSnapshotConsistency("New Feature WIP")]
public static class FermatTrialFactoringKernels
```

## Methods

| Name | Description |
| --- | --- |
| [Kernel_SearchFactors32(uint, uint, uint[], uint[], int)](#kernel_searchfactors32uint-uint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that searches for 32-bit factors of a Fermat number. |

## Kernel_SearchFactors32(uint, uint, uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that searches for 32-bit factors of a Fermat number.

```csharp
[GpuKernel]
public static void Kernel_SearchFactors32(uint startOdd, uint exp, uint[] results, uint[] resultCount, int maxResults);
```

#### Parameters

`startOdd` _uint_<br>
The odd number to start the factor search from.

`exp` _uint_<br>
The exponent of the Fermat number $F_n$.

`results` _uint[]_<br>
The buffer to store the discovered factors.

`resultCount` _uint[]_<br>
The counter buffer indicating how many factors were found.

`maxResults` _int_<br>
The maximum number of factors to store in the `results` buffer.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("FermatTrialFactoringKernels contains static kernel endpoints for factoring ops.");
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
