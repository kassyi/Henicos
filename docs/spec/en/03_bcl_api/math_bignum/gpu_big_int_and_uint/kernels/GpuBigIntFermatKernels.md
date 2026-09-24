---
title: "GpuBigIntFermatKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Fermat number related operations and reductions."
---

## GpuBigIntFermatKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Contains native G# (CUDA) kernels used for Fermat number arithmetic, primarily large modular reductions against Fermat primes on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntFermatKernels
```

## Methods

| Name | Description |
| --- | --- |
| [Kernel_FermatReduce_Mega(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int, int)](#kernel_fermatreduce_megauint-uint-uint-uint-uint-uint-int-int-int-int) | A GPU kernel (`[GpuKernel]`) that performs a large modular reduction using Fermat number properties. |

## Kernel_FermatReduce_Mega(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int, int)

A GPU kernel (`[GpuKernel]`) that performs a large modular reduction using Fermat number properties.

```csharp
[GpuKernel]
public static void Kernel_FermatReduce_Mega(
    uint[] valueT, uint[] mulResult, uint[] quotient, uint[] remainder, uint[] temp,
    uint[] remainderMask, int numWords, int outNumWords, int totalShiftBits, int exponent);
```

#### Parameters

`valueT` _uint[]_<br>
The original target value.

`mulResult` _uint[]_<br>
The multiplication result buffer.

`quotient` _uint[]_<br>
The quotient output buffer.

`remainder` _uint[]_<br>
The remainder output buffer.

`temp` _uint[]_<br>
The temporary working buffer.

`remainderMask` _uint[]_<br>
The bitmask applied to extract the remainder portion.

`numWords` _int_<br>
The base word count.

`outNumWords` _int_<br>
The output word count.

`totalShiftBits` _int_<br>
The total number of bits to shift.

`exponent` _int_<br>
The exponent of the Fermat number $F_n$.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntFermatKernels contains static kernel endpoints for Fermat reduction ops.");
```

## Remarks

This class contains `[GpuKernel]` and `[GpuDeviceFunction]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuContext`.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
