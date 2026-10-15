---
title: "AotAdvancedMathHandler Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Dispatches advanced arbitrary-precision arithmetic operations (NTT multiplication, Lehmer GCD, ModInverse, and Newton-Raphson mega-kernels) to GPU device kernels."
---

## AotAdvancedMathHandler Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Implements `IGpuAdvancedMathProvider` by dispatching high-complexity multi-precision arithmetic operations—including four-step NTT convolution, Lehmer GCD macro-kernels, Extended Binary GCD modular inversion, and cooperative Newton-Raphson reciprocal/inverse-square-root mega-kernels—to compiled G# CUDA kernels.

```csharp
public sealed class AotAdvancedMathHandler : IGpuAdvancedMathProvider
```

## Methods

| Name | Description |
| --- | --- |
| [Mul(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)](#mulgpuworkspace-gpubiguint-uint-gpubiguint) | Launches `GpuBigIntScalarKernels.ScalarMulKernel` to multiply a multi-word operand by a 32-bit unsigned scalar. |
| [Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Executes global non-cooperative dual-modulus NTT multiplication and CRT carry resolution on the GPU. |
| [Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the Greatest Common Divisor using `GpuBigIntGcdKernels.ExecuteLehmerGcdMacroKernel` and `GpuBigIntDivRem.Mod` fallback steps. |
| [ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modinversegpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the modular multiplicative inverse via `GpuBigIntGcdKernels.ExecuteExtendedBinaryGcdMacroKernel` (for odd moduli) or `ModInverseHostLoopController` (for even moduli). |
| [InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int)](#inversesqrtmegagpuworkspace-gpubiguint-gpubiguint-int-int-int) | Launches `GpuBigIntSqrtKernels.InverseSqrtNewtonRaphsonMegaKernel` to compute the fixed-point inverse square root on the GPU. |
| [ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int, int, int)](#reciprocalmegagpuworkspace-gpubiguint-gpubiguint-int-int-int-int-int) | Launches `GpuBigIntDivRemKernels.ReciprocalNewtonRaphsonMegaKernel` to compute the fixed-point reciprocal on the GPU. |
| [ExecuteModPowNttMegaKernel(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt, int, int, uint, uint)](#executemodpownttmegakernelgpuworkspace-gpubiguint-gpubiguint-gpubiguint-gpubiguint-gpubiguint-int-int-uint-uint) | Executes chunked `GpuBigIntDivRemKernels.ModPowNttMegaKernel` launches with per-chunk stream synchronization to prevent TDR watchdog timeouts. |

## Mul(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

## ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

```csharp
public unsafe void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
```

#### Exceptions

`ArithmeticException`  
Thrown when `value` and `modulus` are not coprime ($\gcd \ne 1$).

## InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int)

```csharp
public void InverseSqrtMega(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    int marginWords,
    int actualBits,
    int iterations);
```

## ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, int, int, int, int, int)

```csharp
public void ReciprocalMega(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    int marginWords,
    int numBits,
    int marginBits,
    int iterations,
    int targetBits);
```

## ExecuteModPowNttMegaKernel(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt, int, int, uint, uint)

Executes the ModPow NTT MegaKernel in 8-word exponent chunks.

```csharp
public void ExecuteModPowNttMegaKernel(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    GpuBigUInt constant,
    GpuBigUInt reciprocalMod,
    GpuBigUInt exponent,
    int n,
    int targetBits,
    uint gridDim,
    uint blockDim);
```

## Examples

### Instantiating AotAdvancedMathHandler

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

IGpuAdvancedMathProvider handler = new AotAdvancedMathHandler();
Console.WriteLine($"Initialized handler: {handler.GetType().Name}");
```

## Remarks

`AotAdvancedMathHandler` caches precomputed forward and inverse NTT twiddle factor tables in device memory per transform length $m$ (`ConcurrentDictionary<int, Lazy<TwiddleCache>>`) to avoid redundant host-side root-of-unity generation across repeated multiplications.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
