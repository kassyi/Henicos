---
title: "CSharpCpuBigFloatArithmetic Class (G#)"
site: "G# Documentation"
published: "2026-10-27T00:00:00.000Z"
language: "en"
description: "Host (CPU) execution implementation of arbitrary-precision floating-point arithmetic, the fallback of GSharpKernelFloatProvider."
---

## CSharpCpuBigFloatArithmetic Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides a stateless, thread-safe host (CPU) implementation of [`IGpuBigFloatArithmetic`](IGpuBigFloatArithmetic.md) using `System.Numerics.BigInteger` mantissa scaling and bit shifts.

```csharp
[ExcludeFromCodeCoverage]
public sealed class CSharpCpuBigFloatArithmetic : IGpuBigFloatArithmetic
```

## Fields

| Name | Description |
| --- | --- |
| `Instance` | Gets the singleton instance of `CSharpCpuBigFloatArithmetic`. |

## Methods

Every method reads its operands from device memory, computes on the host and writes the significand of the result back to `resultSig`, whose word count $n$ sets the precision $p = 32n$.

| Name | Description |
| --- | --- |
| [Normalize(...)](#normalizeref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Normalizes a floating-point significand so that its most significant bit is aligned to the target word width. |
| [Add(...)](#addref-gpubigintexecutioncontext-in-gpufloatoperand-in-gpufloatoperand-gpubigintmemoryhandle) | Computes $\mathrm{RZ}_p(x + y)$ on the CPU from the exact sum of the significands aligned at the smaller exponent. |
| [Multiply(...)](#multiplyref-gpubigintexecutioncontext-in-gpufloatoperand-in-gpufloatoperand-gpubigintmemoryhandle) | Computes normalized multiplication of two arbitrary-precision floating-point numbers on the CPU. |
| [Divide(...)](#divideref-gpubigintexecutioncontext-in-gpufloatoperand-in-gpufloatoperand-gpubigintmemoryhandle) | Computes normalized division of two arbitrary-precision floating-point numbers on the CPU. |
| [Reciprocal(...)](#reciprocalref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes the normalized reciprocal ($1 / x$) of an arbitrary-precision floating-point number on the CPU. |
| [Sqrt(...)](#sqrtref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes the normalized square root ($\sqrt{x}$) of a non-negative arbitrary-precision floating-point number on the CPU. |
| [Rsqrt(...)](#rsqrtref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes the normalized reciprocal square root ($1 / \sqrt{x}$) on the CPU. |
| [Exp(...)](#expref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes $\mathrm{RZ}_p(e^x)$ with the same algorithm and the same bits as the GPU provider. |
| [Log(...)](#logref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes $\mathrm{RZ}_p(\ln x)$ with the same algorithm and the same bits as the GPU provider. |
| [Pow(...)](#powref-gpubigintexecutioncontext-in-gpufloatoperand-in-gpufloatoperand-gpubigintmemoryhandle) | Computes $\mathrm{RZ}_p(x^y)$ for a positive base with the same algorithm and the same bits as the GPU provider. |
| [DivideByUInt32(...)](#dividebyuint32ref-gpubigintexecutioncontext-in-gpufloatoperand-uint-gpubigintmemoryhandle) | Divides the significand by a 32-bit unsigned integer scalar and outputs the remainder. |

## Normalize(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Normalize(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

## Add(ref GpuBigIntExecutionContext, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Add(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand left,
    in GpuFloatOperand right,
    GpuBigIntMemoryHandle resultSig);
```

#### Remarks

An operand far below the other one is reduced to a sticky bit by the same rule as on the GPU, so the work does not grow with the exponent difference and the two providers return the same bits (see [Far-Apart Exponents](GSharpKernelFloatProvider.md#far-apart-exponents)).

## Multiply(ref GpuBigIntExecutionContext, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Multiply(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand left,
    in GpuFloatOperand right,
    GpuBigIntMemoryHandle resultSig);
```

## Divide(ref GpuBigIntExecutionContext, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Divide(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand left,
    in GpuFloatOperand right,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`DivideByZeroException`  
Thrown when the divisor `right` is zero.

## Reciprocal(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Reciprocal(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`DivideByZeroException`  
Thrown when `value` is zero.

## Sqrt(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Sqrt(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`InvalidOperationException`  
Thrown when `value` is negative.

## Rsqrt(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Rsqrt(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`InvalidOperationException`  
Thrown when `value` is negative.

`DivideByZeroException`  
Thrown when `value` is zero.

## Exp(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Exp(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`OverflowException`  
Thrown when the exponent of the result does not fit in `int` (an underflow, named in the message, for a large negative argument).

## Log(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Log(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`InvalidOperationException`  
Thrown when `value` is negative or zero.

`OverflowException`  
Thrown when the exponent of the result does not fit in `int`.

## Pow(ref GpuBigIntExecutionContext, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)

```csharp
public GpuFloatResult Pow(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand baseValue,
    in GpuFloatOperand exponent,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`InvalidOperationException`  
Thrown when `baseValue` is negative or zero, whatever the exponent.

`OverflowException`  
Thrown when the exponent of the result does not fit in `int` ("Pow overflow", or "Pow underflow" for a result below 1).

## DivideByUInt32(ref GpuBigIntExecutionContext, in GpuFloatOperand, uint, GpuBigIntMemoryHandle)

```csharp
public GpuFloatScalarDivResult DivideByUInt32(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    uint divisor,
    GpuBigIntMemoryHandle resultSig);
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

`CSharpCpuBigFloatArithmetic` is the fallback strategy inside [`GSharpKernelFloatProvider`](GSharpKernelFloatProvider.md) on hosts without CUDA GPUs (CPU fallback mode), and runs host-only unit tests.

`Exp`, `Log` and `Pow` are not a reference for the GPU: they run the same algorithm as the GPU provider with `BigInteger` (the same host-side plan from the precision and [`GpuBigFloatTranscendentalOptions`](GpuBigFloatTranscendentalOptions.md), the same integer truncations, the same rounding check), so the two providers return the same bits, the exact value rounded toward zero to $p$ bits. `Exp` reduces $x = k \ln 2 + r$ and evaluates a Paterson–Stockmeyer Taylor polynomial at $r / 2^s$ followed by $s$ squarings; `Log` reduces $x = m \cdot 2^e$ and evaluates an $\mathrm{atanh}$ series; `Pow` returns an exactly representable power directly and computes every other one as $e^{y \cdot L}$. The tests check both against an independent interval reference.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Floats (GpuBigFloat)](00_index.md)
- [IGpuBigFloatArithmetic](IGpuBigFloatArithmetic.md)
- [GSharpKernelFloatProvider](GSharpKernelFloatProvider.md)
- [GpuBigFloatTranscendentalOptions](GpuBigFloatTranscendentalOptions.md)
