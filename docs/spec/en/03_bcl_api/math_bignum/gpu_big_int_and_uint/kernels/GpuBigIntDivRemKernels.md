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

## Fields

The correction status codes that a correction writes to `status[1]` (after adding 1 to the failure count in `status[0]`) when it does not converge; the host reports them at the next read of the workspace stream.

| Name | Value | Written by |
| --- | --- | --- |
| `ReduceZeroSyncCorrectionFailed` | 1 | `BarrettCorrectionKernel` |
| `ReciprocalCorrectionFailed` | 2 | The reciprocal correction |
| `InverseSqrtCorrectionFailed` | 3 | The inverse square root correction |
| `SqrtCorrectionFailed` | 4 | The integer square root correction |

## Methods

| Name | Description |
| --- | --- |
| `AddSmallSigned(uint[], int, int)` | A GPU device function (`[GpuDeviceFunction]`) that adds a small signed count to a multi-word unsigned value in place, propagating the carry or the borrow; serial, call it from one thread. |
| [BarrettCorrectionKernel(uint[], uint[], uint[], int, int, uint[])](#barrettcorrectionkerneluint-uint-uint-int-int-uint) | A GPU kernel (`[GpuKernel]`) that corrects the remainder `diff = a - q * b` into `[0, b)` and the quotient by the same number of steps. |
| `CorrectIntoDivisorRange(uint[], uint[], int, int, int, uint[], uint)` | A GPU device function (`[GpuDeviceFunction]`) that moves a signed remainder into `[0, divisor)` one divisor at a time and returns the steps to add to the quotient. |
| `ExecuteNttMultiplyMacro(NewtonRaphsonContext, uint[], uint[], uint[], int, int, int, int)` | A GPU device function (`[GpuDeviceFunction]`) that multiplies two operands by NTT inside the Newton-Raphson mega-kernels. |
| `InitializeOneModuloKernel(uint[], uint[], int)` | A GPU kernel (`[GpuKernel]`) that writes the low word of `1 mod m` into `result[0]`; the caller clears the other words. Launch with one block of one thread. |
| [ModPowNttMegaKernel(ModPowNttContext)](#modpownttmegakernelmodpownttcontext) | A cooperative GPU mega-kernel (`[GpuKernel(Cooperative = true)]`) that executes multi-bit binary exponentiation with dual-prime NTT multiplication and Barrett reduction on the GPU. |
| [ReciprocalNewtonRaphsonMegaKernel(NewtonRaphsonContext)](#reciprocalnewtonraphsonmegakernelnewtonraphsoncontext) | A cooperative GPU mega-kernel (`[GpuKernel(Cooperative = true)]`) that executes iterative Newton-Raphson reciprocal refinement using grid-wide synchronization and NTT multiplication. |
| `TruncatedBoundCorrectionKernel(uint[], int, uint[], int, uint[], int)` | A GPU kernel (`[GpuKernel]`) that subtracts 1 from `estimate` when `product` is greater than `bound`: the correction of a quotient or an inverse square root computed from a truncated operand. |

## BarrettCorrectionKernel(uint[], uint[], uint[], int, int, uint[])

A GPU kernel (`[GpuKernel]`) that corrects the remainder `diff = a - q * b` and the quotient `quotient` by the same number of steps, so that both are exact when the estimate is within 1000 steps of `q`. Launched with one block; runs on the stream without a host synchronization. When the estimate is further away, `ReduceZeroSyncCorrectionFailed` is written to `status`. The correction ends when $0 \le \text{diff} < \text{rightOperand}$.

```csharp
[GpuKernel]
public static void BarrettCorrectionKernel(uint[] diff, [GpuReadOnly] uint[] rightOperand, uint[] quotient,
    int evalWords, int quotientWords, uint[] status);
```

#### Parameters

`diff` _uint[]_<br>
The remainder buffer to correct in-place.

`rightOperand` _uint[]_<br>
The divisor/modulus limb buffer.

`quotient` _uint[]_<br>
The quotient estimate, corrected in place.

`evalWords` _int_<br>
The number of words evaluated during correction.

`quotientWords` _int_<br>
The word count of `quotient`.

`status` _uint[]_<br>
The workspace's correction status words.

## ModPowNttMegaKernel(ModPowNttContext)

A cooperative GPU mega-kernel (`[GpuKernel(Cooperative = true)]`) that executes modular exponentiation over a range of exponent words using dual-prime NTT multiplication and in-kernel Barrett reduction.

```csharp
[GpuKernel(Cooperative = true)]
public static void ModPowNttMegaKernel(ModPowNttContext ctx);
```

#### Parameters

`ctx` _ModPowNttContext_<br>
The context struct containing NTT buffers, precomputed modulus reciprocal, exponent words, and workspace buffers.

## ReciprocalNewtonRaphsonMegaKernel(NewtonRaphsonContext)

A cooperative GPU mega-kernel (`[GpuKernel(Cooperative = true)]`) that executes iterative Newton-Raphson reciprocal refinement ($X_{k+1} = X_k (2 - D \cdot X_k)$) using grid-wide synchronization and dual-prime NTT multiplication.

```csharp
[GpuKernel(Cooperative = true)]
public static void ReciprocalNewtonRaphsonMegaKernel(NewtonRaphsonContext core);
```

#### Parameters

`core` _NewtonRaphsonContext_<br>
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
