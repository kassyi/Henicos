---
title: "GpuBigIntFermatKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Fermat number related operations and reductions."
---

## GpuBigIntFermatKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

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
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntFermatKernels contains static kernel endpoints for Fermat reduction ops.");
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
