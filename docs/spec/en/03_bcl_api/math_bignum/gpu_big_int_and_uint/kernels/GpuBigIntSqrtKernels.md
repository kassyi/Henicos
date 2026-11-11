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
| [InverseSqrtNewtonRaphsonMegaKernel(NewtonRaphsonContext)](#inversesqrtnewtonraphsonmegakernelnewtonraphsoncontext) | A cooperative GPU kernel (`[GpuKernel(Cooperative = true)]`) that executes the Newton-Raphson inverse square root iteration loop on the GPU. |
| [SqrtFloorCorrectionKernel(uint[], uint[], uint[], uint[], uint[], int, int, int, uint[])](#sqrtfloorcorrectionkerneluint-uint-uint-uint-uint-int-int-int-uint) | A GPU kernel (`[GpuKernel]`) that moves an integer square root estimate to `floor(sqrt(a))` one step at a time. |
| [CorrectInverseSqrtSlack(uint[], uint[], uint[], uint[], int, int, int, int, uint[], uint)](#correctinversesqrtslackuint-uint-uint-uint-int-int-int-int-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that corrects an inverse square root estimate by its slack and returns the steps to add to it. |

## InverseSqrtNewtonRaphsonMegaKernel(NewtonRaphsonContext)

A cooperative GPU kernel (`[GpuKernel(Cooperative = true)]`) that executes the Newton-Raphson inverse square root iteration loop entirely on the GPU using grid-wide synchronization (`Gpu.SyncGrid`).

```csharp
[GpuKernel(Cooperative = true)]
public static void InverseSqrtNewtonRaphsonMegaKernel(NewtonRaphsonContext core);
```

#### Parameters

`core` _NewtonRaphsonContext_<br>
The Newton-Raphson context structure containing operand buffers (`TargetOperand`, `ResultBuffer`, `Constant3`), precision dimensions (`NumberOfWords`, `MarginWords`, `MarginBits`, `Iterations`), NTT twiddle factors, and intermediate workspace buffers.

## SqrtFloorCorrectionKernel(uint[], uint[], uint[], uint[], uint[], int, int, int, uint[])

A GPU kernel (`[GpuKernel]`) that moves an integer square root estimate `r` of `a` to `floor(sqrt(a))`, one step at a time, by updating the slack `s = a - r^2`: `r` is the floor exactly when `0 <= s < 2r + 1`. Launched with one block; runs on the stream without a host synchronization. When `r` is not confirmed within `maxSteps` steps, thread 0 adds 1 to `status[0]` and writes `GpuBigIntDivRemKernels.SqrtCorrectionFailed` to `status[1]`.

```csharp
[GpuKernel]
public static void SqrtFloorCorrectionKernel([GpuReadOnly] uint[] operand, [GpuReadOnly] uint[] rootSquared,
    uint[] root, uint[] slack, uint[] trial, int operandWords, int rootWords, int maxSteps, uint[] status);
```

`operand` and `rootSquared` (`r^2`) have `operandWords` words; `root` has `rootWords` words and is corrected in place; `slack` and `trial` are scratch of `operandWords + 1` words.

## CorrectInverseSqrtSlack(uint[], uint[], uint[], uint[], int, int, int, int, uint[], uint)

A GPU device function (`[GpuDeviceFunction]`) that corrects an inverse square root estimate `x` by updating the slack `s = 2^(2B) - a * x^2` and the product `a * x`, and returns the number of steps to add to `x`. Runs on block 0 only (the other blocks return 0 at once). When `x` is not confirmed within `maxSteps` steps, thread 0 adds 1 to `status[0]` and writes `failureCode` to `status[1]`.

```csharp
[GpuDeviceFunction]
public static int CorrectInverseSqrtSlack(uint[] slack, uint[] ax, [GpuReadOnly] uint[] operand, uint[] trial,
    int slackWords, int axWords, int operandWords, int maxSteps, uint[] status, uint failureCode);
```

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Cooperative Kernel (GPU)
var ctx = new NewtonRaphsonContext
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
