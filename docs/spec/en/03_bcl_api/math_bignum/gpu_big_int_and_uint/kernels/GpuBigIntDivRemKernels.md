---
title: "GpuBigIntDivRemKernels Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "CUDA cooperative mega-kernels and Barrett correction kernels for Newton-Raphson division and NTT modular exponentiation."
---

## GpuBigIntDivRemKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) cooperative mega-kernels and Barrett reduction correction kernels used by `GpuBigIntDivRem` for Newton-Raphson reciprocal computation, division with remainder, and NTT-accelerated modular exponentiation (`ModPow`).

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigIntDivRemKernels
```

## Methods

| Name | Description |
| --- | --- |
| [BarrettCorrectionKernel(uint[], uint[], int)](#barrettcorrectionkerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that iteratively corrects the remainder after approximate Barrett quotient subtraction. |
| [ModPowNttMegaKernel(ModPowNttContext)](#modpownttmegakernelmodpownttcontext) | A cooperative GPU mega-kernel (`[GpuKernel(Cooperative = true)]`) that executes multi-bit binary exponentiation with dual-prime NTT multiplication and Barrett reduction on the GPU. |
| [ReciprocalNewtonRaphsonMegaKernel(ReciprocalNewtonRaphson)](#reciprocalnewtonraphsonmegakernelreciprocalnewtonraphson) | A cooperative GPU mega-kernel (`[GpuKernel(Cooperative = true)]`) that executes iterative Newton-Raphson reciprocal refinement using grid-wide synchronization and NTT multiplication. |

## BarrettCorrectionKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that iteratively corrects the remainder (`diff`) by adding or subtracting `rightOperand` until $0 \le \text{diff} < \text{rightOperand}$.

```csharp
[GpuKernel]
public static void BarrettCorrectionKernel(uint[] diff, uint[] rightOperand, int evalWords);
```

#### Parameters

`diff` _uint[]_<br>
The remainder buffer to correct in-place.

`rightOperand` _uint[]_<br>
The divisor/modulus limb buffer.

`evalWords` _int_<br>
The number of words evaluated during correction.

## ModPowNttMegaKernel(ModPowNttContext)

A cooperative GPU mega-kernel (`[GpuKernel(Cooperative = true)]`) that executes modular exponentiation over a range of exponent words using dual-prime NTT multiplication and in-kernel Barrett reduction.

```csharp
[GpuKernel(Cooperative = true)]
public static void ModPowNttMegaKernel(ModPowNttContext ctx);
```

#### Parameters

`ctx` _ModPowNttContext_<br>
The context struct containing NTT buffers, precomputed modulus reciprocal, exponent words, and workspace buffers.

## ReciprocalNewtonRaphsonMegaKernel(ReciprocalNewtonRaphson)

A cooperative GPU mega-kernel (`[GpuKernel(Cooperative = true)]`) that executes iterative Newton-Raphson reciprocal refinement ($X_{k+1} = X_k (2 - D \cdot X_k)$) using grid-wide synchronization and dual-prime NTT multiplication.

```csharp
[GpuKernel(Cooperative = true)]
public static void ReciprocalNewtonRaphsonMegaKernel(ReciprocalNewtonRaphson ctx);
```

#### Parameters

`ctx` _ReciprocalNewtonRaphson_<br>
The context struct containing NTT twiddle factors, operand buffers, and iteration parameters.

## Examples

### Referencing GpuBigIntDivRemKernels Endpoints

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: Host (CPU) or G# Kernel (GPU)
// GpuBigIntDivRemKernels provides cooperative mega-kernels launched by GpuBigIntDivRem.
Console.WriteLine($"DivRem mega-kernel container: {typeof(GpuBigIntDivRemKernels).Name}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntDivRem Class](../GpuBigIntDivRem.md)
- [DivRem Algorithm](../algorithms/DivRem.md)
- [ModPow Algorithm](../algorithms/ModPow.md)
- [GpuBigIntNttKernels Class](GpuBigIntNttKernels.md)
