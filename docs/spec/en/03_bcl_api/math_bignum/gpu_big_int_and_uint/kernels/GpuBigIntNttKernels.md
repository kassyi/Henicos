---
title: "GpuBigIntNttKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Number Theoretic Transform (NTT) core algorithms used in large integer multiplication."
---

## GpuBigIntNttKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains the core GPU functions and constants required for performing the Number Theoretic Transform (NTT). Note that this class only defines `[GpuKernel]` methods that are explicitly designed to be called from the host.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntNttKernels
```

## Methods

| Name | Description |
| --- | --- |
| [Kernel_ResolveNttCarries_Blocked(uint[], uint[], uint[], uint[], int, int, int)](#kernel_resolventtcarries_blockeduint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that resolves NTT carries in a block-wise manner. |
| [Kernel_ResolveNttCarries_Apply(uint[], uint[], int, int, int)](#kernel_resolventtcarries_applyuint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that applies resolved NTT carries across blocks. |

## Kernel_ResolveNttCarries_Blocked(uint[], uint[], uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that resolves NTT carries in a block-wise manner.

```csharp
[GpuKernel]
public static void Kernel_ResolveNttCarries_Blocked(uint[] uncarriedLow, uint[] uncarriedHigh, uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize);
```

#### Parameters

`uncarriedLow` _uint[]_<br>
The lower 32-bits of the uncarried coefficients.

`uncarriedHigh` _uint[]_<br>
The upper 32-bits of the uncarried coefficients.

`outputBuffer` _uint[]_<br>
The buffer to store the output after block carries are resolved.

`blockCarries` _uint[]_<br>
The buffer to store intermediate block carry values.

`transformLength` _int_<br>
The length of the NTT transform.

`maxWordCount` _int_<br>
The maximum word count.

`chunkSize` _int_<br>
The size of each chunk processed per block.

## Kernel_ResolveNttCarries_Apply(uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that applies resolved NTT carries across blocks.

```csharp
[GpuKernel]
public static void Kernel_ResolveNttCarries_Apply(uint[] outputBuffer, uint[] blockCarries, int maxWordCount, int chunkSize, int numBlocks);
```

#### Parameters

`outputBuffer` _uint[]_<br>
The output buffer to which block carries are added.

`blockCarries` _uint[]_<br>
The buffer containing the intermediate block carry values.

`maxWordCount` _int_<br>
The maximum word count.

`chunkSize` _int_<br>
The size of each chunk.

`numBlocks` _int_<br>
The total number of blocks.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntNttKernels contains static kernel endpoints for NTT.");
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
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
- [7. Stdlib Math and Lazy Evaluation](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
