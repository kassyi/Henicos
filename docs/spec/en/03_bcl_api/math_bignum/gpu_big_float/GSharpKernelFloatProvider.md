---
title: "GSharpKernelFloatProvider Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Execution provider implementing arbitrary-precision floating-point arithmetic via native CUDA kernels, GSharp AOT kernels, and CPU fallback strategies."
---

## GSharpKernelFloatProvider Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Execution provider implementing [`IGpuBigFloatArithmetic`](IGpuBigFloatArithmetic.md) by executing high-precision floating-point operations natively on the GPU via compiled G# device kernels (`GpuBigFloatKernels`, `GpuBigIntMath`, `GpuBigIntDivRem`), with CPU fallback orchestration ([`CSharpCpuBigFloatArithmetic`](CSharpCpuBigFloatArithmetic.md)).

```csharp
public sealed class GSharpKernelFloatProvider(IGpuBigFloatArithmetic fallbackStrategy)
    : IGpuBigFloatArithmetic
```

## Properties

| Name | Description |
| --- | --- |
| `Instance` | Gets the default singleton instance of `GSharpKernelFloatProvider` using `CSharpCpuBigFloatArithmetic.Instance` as fallback. |

## Methods

| Name | Description |
| --- | --- |
| [CreateGSharpKernel()](#creategsharpkernel) | Gets the singleton instance of `GSharpKernelFloatProvider`. |
| `Normalize(...)` | Normalizes a floating-point significand so that its most significant bit is aligned to the target word width. |
| `Add(...)` | Computes radix-aligned signed addition of two arbitrary-precision floating-point numbers. |
| `Multiply(...)` | Computes normalized multiplication of two arbitrary-precision floating-point numbers. |
| `Divide(...)` | Computes normalized division using hybrid routing via G# device kernels and Newton-Raphson reciprocals. |
| `Reciprocal(...)` | Computes the normalized reciprocal ($1 / x$) via G# device kernels. |
| `Sqrt(...)` | Computes the normalized square root ($\sqrt{x}$) via G# device kernels or fallback strategy. |
| `Rsqrt(...)` | Computes the normalized reciprocal square root ($1 / \sqrt{x}$) by composing `Sqrt` and `Reciprocal`. |
| `Exp(...)` | Computes the natural exponential ($e^x$) via the fallback strategy. |
| `Log(...)` | Computes the natural logarithm ($\ln(x)$) via the fallback strategy. |
| `Pow(...)` | Computes $x^y$ via the fallback strategy. |
| `DivideByUInt32(...)` | Divides the significand by a 32-bit unsigned integer scalar and outputs the remainder. |

> [!NOTE]
> Legacy native CUDA P/Invoke routines have been decoupled into the internal `NativeFloatProvider` (marked `[Obsolete]`). Pure G# device kernels are now exclusively dispatched by `GSharpKernelFloatProvider`.

## CreateGSharpKernel()

Creates an AOT-compiled provider that uses G# kernels for high-precision floating-point operations.

```csharp
public static GSharpKernelFloatProvider CreateGSharpKernel();
```

#### Returns

_GSharpKernelFloatProvider_  
A new `GSharpKernelFloatProvider` instance configured for G# kernel operations.

## Examples

### Creating a GSharpKernelFloatProvider Instance

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

IGpuBigFloatArithmetic provider = GSharpKernelFloatProvider.CreateGSharpKernel();
Console.WriteLine($"Created float provider: {provider.GetType().Name}");
```

## Remarks

`GSharpKernelFloatProvider` manages temporary device scratch buffers deterministically using `context.GpuWorkspace.RentTemp(...)` without managed heap allocations on the GPU hot path.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Floats (GpuBigFloat)](00_index.md)
- [IGpuBigFloatArithmetic](IGpuBigFloatArithmetic.md)
- [CSharpCpuBigFloatArithmetic](CSharpCpuBigFloatArithmetic.md)
