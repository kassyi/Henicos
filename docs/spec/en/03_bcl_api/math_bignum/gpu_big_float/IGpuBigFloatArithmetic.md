---
title: "IGpuBigFloatArithmetic Interface (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Defines the contract for arbitrary-precision floating-point arithmetic operations executed across GPU and CPU backends."
---

## IGpuBigFloatArithmetic Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines the contract for arbitrary-precision floating-point arithmetic operations executed across GPU and CPU backends.

```csharp
public interface IGpuBigFloatArithmetic
```

## Methods

| Name | Description |
| --- | --- |
| [Add(...)](#addref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Adds two arbitrary-precision floating-point operands. |
| [Divide(...)](#divideref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Divides one arbitrary-precision floating-point operand by another. |
| [DivideByUInt32(...)](#dividebyuint32ref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-uint-gpubigintmemoryhandle-out-int-out-bool-out-uint) | Divides an arbitrary-precision floating-point value by a 32-bit unsigned integer scalar. |
| [Exp(...)](#expref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the natural exponential ($e^x$) of an arbitrary-precision floating-point value. |
| [Log(...)](#logref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the natural logarithm ($\ln(x)$) of an arbitrary-precision floating-point value. |
| [Multiply(...)](#multiplyref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Multiplies two arbitrary-precision floating-point operands. |
| [Normalize(...)](#normalizeref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Normalizes an arbitrary-precision floating-point value such that the most significant limb of the significand is non-zero. |
| [Pow(...)](#powref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Raises an arbitrary-precision floating-point base to an arbitrary-precision floating-point exponent ($\text{base}^{\text{exp}}$). |
| [Reciprocal(...)](#reciprocalref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the reciprocal ($1 / x$) of an arbitrary-precision floating-point value. |
| [Rsqrt(...)](#rsqrtref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the reciprocal square root ($1 / \sqrt{x}$) of an arbitrary-precision floating-point value. |
| [Sqrt(...)](#sqrtref-gpubigintexecutioncontext-gpubigintmemoryhandle-int-bool-gpubigintmemoryhandle-out-int-out-bool) | Computes the square root ($\sqrt{x}$) of an arbitrary-precision floating-point value. |

## Add(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Adds two arbitrary-precision floating-point operands.

```csharp
void Add(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
    GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Divide(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Divides one arbitrary-precision floating-point operand by another.

```csharp
void Divide(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
    GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## DivideByUInt32(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, uint, GpuBigIntMemoryHandle, out int, out bool, out uint)

Divides an arbitrary-precision floating-point value by a 32-bit unsigned integer scalar.

```csharp
void DivideByUInt32(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    uint divisor,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign, out uint remainder);
```

## Exp(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Computes the natural exponential ($e^x$) of an arbitrary-precision floating-point value.

```csharp
void Exp(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Log(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Computes the natural logarithm ($\ln(x)$) of an arbitrary-precision floating-point value.

```csharp
void Log(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Multiply(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Multiplies two arbitrary-precision floating-point operands.

```csharp
void Multiply(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle leftSig, int leftExp, bool leftSign,
    GpuBigIntMemoryHandle rightSig, int rightExp, bool rightSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Normalize(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Normalizes an arbitrary-precision floating-point value such that the most significant limb of the significand is non-zero.

```csharp
void Normalize(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Pow(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Raises an arbitrary-precision floating-point base to an arbitrary-precision floating-point exponent ($\text{base}^{\text{exp}}$).

```csharp
void Pow(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle baseSig, int baseExp, bool baseSign,
    GpuBigIntMemoryHandle expSig, int exponentExp, bool exponentSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Reciprocal(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Computes the reciprocal ($1 / x$) of an arbitrary-precision floating-point value.

```csharp
void Reciprocal(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Rsqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Computes the reciprocal square root ($1 / \sqrt{x}$) of an arbitrary-precision floating-point value.

```csharp
void Rsqrt(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Sqrt(ref GpuBigIntExecutionContext, GpuBigIntMemoryHandle, int, bool, GpuBigIntMemoryHandle, out int, out bool)

Computes the square root ($\sqrt{x}$) of an arbitrary-precision floating-point value.

```csharp
void Sqrt(
    ref GpuBigIntExecutionContext context,
    GpuBigIntMemoryHandle valueSig, int valueExp, bool valueSign,
    GpuBigIntMemoryHandle resultSig, out int resultExp, out bool resultSign);
```

## Remarks

This interface abstracts low-level mantissa and exponent transformations for [GpuBigFloat](GpuBigFloat.md) and [GpuBigUFloat](GpuBigUFloat.md) representations, operating directly on raw memory handles (`GpuBigIntMemoryHandle`) via a [GpuBigIntExecutionContext](../gpu_big_int_and_uint/GpuBigIntExecutionContext.md).

## Examples

### Evaluating Floating-Point Expressions with an Arithmetic Provider

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

using var a = new GpuBigFloat(new GpuBigUFloat(GpuBigUInt.FromBigInteger(workspace, new BigInteger(100), minWords: 4), 0), false);
using var b = new GpuBigFloat(new GpuBigUFloat(GpuBigUInt.FromBigInteger(workspace, new BigInteger(25), minWords: 4), 0), false);

// GpuBigFloatExpressionVisitor dispatches operations through IGpuBigFloatArithmetic
var visitor = new GpuBigFloatExpressionVisitor(workspace);
IGpuBigFloatArithmetic arithmetic = visitor.Arithmetic;

using var sum = (a + b).Evaluate(ref visitor);
Console.WriteLine($"Provider: {arithmetic.GetType().Name}, Sum Significand: {sum.UFloat.Significand.ToBigInteger()}");
```

## See also

### BCL API > Math Bignum
- [GpuBigFloat Struct](GpuBigFloat.md)
- [GpuBigUFloat Struct](GpuBigUFloat.md)
- [FloatExpressions](../gpu_big_int_and_uint/expressions/FloatExpressions.md)
- [GpuBigIntExecutionContext Struct](../gpu_big_int_and_uint/GpuBigIntExecutionContext.md)
