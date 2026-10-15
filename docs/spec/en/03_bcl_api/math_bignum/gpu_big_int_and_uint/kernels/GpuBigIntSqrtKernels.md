---
title: "GpuBigIntSqrtKernels Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "CUDA cooperative mega-kernel implementation for large integer inverse square root using Newton-Raphson iterations and NTT multiplication."
---

## GpuBigIntSqrtKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains the cooperative G# (CUDA) mega-kernel used for computing arbitrary-precision inverse square roots via coupled Newton-Raphson iterations ($X_{k+1} = X_k (3 - A X_k^2) / 2$) accelerated by dual-modulus Number Theoretic Transform (NTT) convolutions.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntSqrtKernels
```

## Methods

| Name | Description |
| --- | --- |
| [InverseSqrtNewtonRaphsonMegaKernel(SqrtNewtonRaphson)](#inversesqrtnewtonraphsonmegakernelsqrtnewtonraphson) | A cooperative GPU kernel (`[GpuKernel(Cooperative = true)]`) that executes the Newton-Raphson inverse square root iteration loop on the GPU. |

## InverseSqrtNewtonRaphsonMegaKernel(SqrtNewtonRaphson)

A cooperative GPU kernel (`[GpuKernel(Cooperative = true)]`) that executes the Newton-Raphson inverse square root iteration loop entirely on the GPU using grid-wide synchronization (`Gpu.SyncGrid`).

```csharp
[GpuKernel(Cooperative = true)]
public static void InverseSqrtNewtonRaphsonMegaKernel(SqrtNewtonRaphson ctx);
```

#### Parameters

`ctx` _SqrtNewtonRaphson_<br>
The Newton-Raphson square root context structure containing operand buffers (`TargetOperand`, `ResultBuffer`, `Constant3`), precision dimensions (`NumberOfWords`, `MarginWords`, `MarginBits`, `Iterations`), NTT twiddle factors, and intermediate workspace buffers.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Cooperative Kernel (GPU)
var ctx = new SqrtNewtonRaphson
{
    Dimensions = new DimensionContext { NumberOfWords = 256, Iterations = 8 }
};
Console.WriteLine($"{nameof(GpuBigIntSqrtKernels)}: Words={ctx.Dimensions.NumberOfWords}, Iterations={ctx.Dimensions.Iterations}");
```

## Remarks

`InverseSqrtNewtonRaphsonMegaKernel` is marked with `[GpuKernel(Cooperative = true)]` and requires CUDA Cooperative Groups launch support so that multi-block NTT steps and carry resolutions can synchronize across the entire grid without returning control to the CPU host between iterations.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuBigIntNttKernels](GpuBigIntNttKernels.md)
