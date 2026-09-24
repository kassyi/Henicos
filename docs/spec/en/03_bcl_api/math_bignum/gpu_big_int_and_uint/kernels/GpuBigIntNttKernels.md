---
title: "GpuBigIntNttKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Number Theoretic Transform (NTT) core algorithms used in large integer multiplication."
---

## GpuBigIntNttKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

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
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntNttKernels contains static kernel endpoints for NTT.");
```

## Remarks

This class contains `[GpuKernel]` and `[GpuDeviceFunction]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuContext`.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
