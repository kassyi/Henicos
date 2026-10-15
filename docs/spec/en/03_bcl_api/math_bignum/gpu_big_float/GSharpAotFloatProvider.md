---
title: "GSharpAotFloatProvider Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Execution provider implementing arbitrary-precision floating-point arithmetic via native CUDA kernels, GSharp AOT kernels, and CPU fallback strategies."
---

## GSharpAotFloatProvider Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Execution provider implementing [`IGpuBigFloatArithmetic`](IGpuBigFloatArithmetic.md) by routing high-precision floating-point operations to native CUDA kernels, GSharp AOT-transpiled `GpuBigIntDivRem` kernels, or a configured fallback strategy ([`CSharpCpuBigFloatArithmetic`](CSharpCpuBigFloatArithmetic.md)).

```csharp
public sealed class GSharpAotFloatProvider(bool useNativeCuda, IGpuBigFloatArithmetic fallbackStrategy)
    : IGpuBigFloatArithmetic
```

## Constructors

| Name | Description |
| --- | --- |
| `GSharpAotFloatProvider(bool, IGpuBigFloatArithmetic)` | Initializes a new instance of `GSharpAotFloatProvider` with the specified native CUDA toggle and fallback arithmetic strategy. |

## Methods

| Name | Description |
| --- | --- |
| [CreateNativeCuda()](#createnativecuda) | Creates a `GSharpAotFloatProvider` configured to dispatch native CUDA P/Invoke kernels with `CSharpCpuBigFloatArithmetic.Instance` as fallback. |
| [CreateGSharpKernel()](#creategsharpkernel) | Creates a `GSharpAotFloatProvider` configured to use GSharp AOT kernels (`GpuBigIntDivRem`) and `CSharpCpuBigFloatArithmetic.Instance` fallback. |
| `Normalize(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Normalizes a floating-point significand so that its most significant bit is aligned to the target word width. |
| `Add(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes radix-aligned signed addition of two arbitrary-precision floating-point numbers. |
| `Multiply(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes normalized multiplication of two arbitrary-precision floating-point numbers. |
| `Divide(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes normalized division using hybrid routing (reciprocal multiplication for $\le 1024$ words in native mode, or `GpuBigIntDivRem.DivRem` for mega division). |
| `Reciprocal(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes the normalized reciprocal ($1 / x$) via native CUDA or `GpuBigIntDivRem.Reciprocal`. |
| `Sqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes the normalized square root ($\sqrt{x}$) via `GpuBigIntDivRem.Sqrt` or the fallback strategy. |
| `Rsqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes the normalized reciprocal square root ($1 / \sqrt{x}$) by composing `Sqrt` and `Reciprocal`. |
| `Exp(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes the natural exponential ($e^x$) via the fallback strategy. |
| `Log(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes the natural logarithm ($\ln(x)$) via the fallback strategy. |
| `Pow(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)` | Computes $x^y$ via the fallback strategy. |
| `DivideByUInt32(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, uint, GpuBigIntMemoryHandle, out int, out bool, out uint)` | Divides the significand by a 32-bit unsigned integer scalar and outputs the remainder. |

## CreateNativeCuda()

Creates an interop provider that uses native CUDA kernels for high-precision floating-point operations.

```csharp
public static GSharpAotFloatProvider CreateNativeCuda();
```

#### Returns

_GSharpAotFloatProvider_  
A new `GSharpAotFloatProvider` instance configured for native CUDA operations.

## CreateGSharpKernel()

Creates an AOT-compiled provider that uses G# kernels for high-precision floating-point operations.

```csharp
public static GSharpAotFloatProvider CreateGSharpKernel();
```

#### Returns

_GSharpAotFloatProvider_  
A new `GSharpAotFloatProvider` instance configured for G# kernel operations.

## Examples

### Creating a GSharpAotFloatProvider Instance

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

IGpuBigFloatArithmetic provider = GSharpAotFloatProvider.CreateGSharpKernel();
Console.WriteLine($"Created float provider: {provider.GetType().Name}");
```

## Remarks

`GSharpAotFloatProvider` manages temporary device scratch buffers deterministically using `context.GpuWorkspace.RentTemp(...)` without managed heap allocations on the GPU hot path.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Floats (GpuBigFloat)](00_index.md)
- [IGpuBigFloatArithmetic](IGpuBigFloatArithmetic.md)
- [CSharpCpuBigFloatArithmetic](CSharpCpuBigFloatArithmetic.md)
