---
title: "CSharpCpuBigFloatArithmetic Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Reference host (CPU) execution implementation for arbitrary-precision floating-point arithmetic."
---

## CSharpCpuBigFloatArithmetic Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides a stateless, thread-safe reference host (CPU) execution implementation of [`IGpuBigFloatArithmetic`](IGpuBigFloatArithmetic.md) using `System.Numerics.BigInteger` mantissa scaling and bit shifts.

```csharp
[ExcludeFromCodeCoverage]
public sealed class CSharpCpuBigFloatArithmetic : IGpuBigFloatArithmetic
```

## Fields

| Name | Description |
| --- | --- |
| `Instance` | Gets the singleton instance of `CSharpCpuBigFloatArithmetic`. |

## Methods

| Name | Description |
| --- | --- |
| [Normalize(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#normalizeref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Normalizes a floating-point significand so that its most significant bit is aligned to the target word width. |
| [Add(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#addref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes radix-aligned signed addition of two arbitrary-precision floating-point numbers on the CPU. |
| [Multiply(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#multiplyref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes normalized multiplication of two arbitrary-precision floating-point numbers on the CPU. |
| [Divide(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#divideref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes normalized division of two arbitrary-precision floating-point numbers on the CPU. |
| [Reciprocal(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#reciprocalref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the normalized reciprocal ($1 / x$) of an arbitrary-precision floating-point number on the CPU. |
| [Sqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#sqrtref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the normalized square root ($\sqrt{x}$) of a non-negative arbitrary-precision floating-point number on the CPU. |
| [Rsqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#rsqrtref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the normalized reciprocal square root ($1 / \sqrt{x}$) on the CPU. |
| [Exp(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#expref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the natural exponential ($e^x$) using fixed-point Taylor series expansion on the CPU. |
| [Log(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#logref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the natural logarithm ($\ln(x)$) using hyperbolic artanh series expansion on the CPU. |
| [Pow(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)](#powref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes $x^y = \exp(y \cdot \ln(x))$ for a positive base on the CPU. |
| [DivideByUInt32(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, uint, GpuBigIntMemoryHandle, out int, out bool, out uint)](#dividebyuint32ref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-uint-gpubigintmemoryhandle-out-int-out-bool-out-uint) | Divides the significand by a 32-bit unsigned integer scalar and outputs the remainder. |

## Normalize(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Normalize(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Add(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Add(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
    GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Multiply(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Multiply(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
    GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Divide(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Divide(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
    GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

#### Exceptions

`DivideByZeroException`  
Thrown when `rightSig` evaluates to zero.

## Reciprocal(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Reciprocal(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

#### Exceptions

`DivideByZeroException`  
Thrown when `valueSig` evaluates to zero.

## Sqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Sqrt(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

#### Exceptions

`InvalidOperationException`  
Thrown when `valueSign` is `true` (negative input).

## Rsqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Rsqrt(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

#### Exceptions

`InvalidOperationException`  
Thrown when `valueSign` is `true` (negative input).

## Exp(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Exp(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Log(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Log(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

#### Exceptions

`InvalidOperationException`  
Thrown when `valueSign` is `true` or `valueSig` is zero.

## Pow(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

```csharp
public void Pow(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle baseSig, int baseExp, bool baseSign,
    GpuBigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

#### Exceptions

`InvalidOperationException`  
Thrown when `baseSign` is `true`.

## DivideByUInt32(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, uint, GpuBigIntMemoryHandle, out int, out bool, out uint)

```csharp
public void DivideByUInt32(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    uint divisor,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign, out uint remainder);
```

#### Exceptions

`DivideByZeroException`  
Thrown when `divisor` is `0`.

## Examples

### Accessing the Singleton CPU Float Provider

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

IGpuBigFloatArithmetic provider = CSharpCpuBigFloatArithmetic.Instance;
using var workspace = new GpuWorkspace(maxWords: 64, engine: GpuExecutionEngine.GSharpAot);
var a = new GpuBigFloat(new GpuBigUInt(workspace, new uint[] { 7 }), exponent: -1, isNegative: false);
var b = new GpuBigFloat(new GpuBigUInt(workspace, new uint[] { 2 }), exponent: 0, isNegative: false);
using GpuBigFloat prod = a * b;
Console.WriteLine($"Provider={provider.GetType().Name}, Exp={prod.UFloat.Exponent}, Neg={prod.IsNegative}");
```

## Remarks

`CSharpCpuBigFloatArithmetic` serves as the reference implementation for Tier-1 deterministic unit tests and as the fallback strategy inside [`GSharpAotFloatProvider`](GSharpAotFloatProvider.md) when running on hosts without CUDA GPUs or for transcendental functions (`Exp`, `Log`, `Pow`).

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Floats (GpuBigFloat)](00_index.md)
- [IGpuBigFloatArithmetic](IGpuBigFloatArithmetic.md)
- [GSharpAotFloatProvider](GSharpAotFloatProvider.md)
