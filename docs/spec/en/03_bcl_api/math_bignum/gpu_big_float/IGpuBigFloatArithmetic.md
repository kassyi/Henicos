---
title: "IGpuBigFloatArithmetic Interface (G#)"
site: "G# Documentation"
published: "2026-10-27T00:00:00.000Z"
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
| [Add(...)](#addref-gpubigintexecutioncontext-in-gpufloatoperand-in-gpufloatoperand-gpubigintmemoryhandle) | Adds two arbitrary-precision floating-point operands. |
| [Divide(...)](#divideref-gpubigintexecutioncontext-in-gpufloatoperand-in-gpufloatoperand-gpubigintmemoryhandle) | Divides one arbitrary-precision floating-point operand by another. |
| [DivideByUInt32(...)](#dividebyuint32ref-gpubigintexecutioncontext-in-gpufloatoperand-uint-gpubigintmemoryhandle) | Divides an arbitrary-precision floating-point value by a 32-bit unsigned integer scalar. |
| [Exp(...)](#expref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes the natural exponential ($e^x$), rounded toward zero to the result precision. |
| [Log(...)](#logref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes the natural logarithm ($\ln x$), rounded toward zero to the result precision. |
| [Multiply(...)](#multiplyref-gpubigintexecutioncontext-in-gpufloatoperand-in-gpufloatoperand-gpubigintmemoryhandle) | Multiplies two arbitrary-precision floating-point operands. |
| [Normalize(...)](#normalizeref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Normalizes a value so that the most significant bit of the significand is the top bit of the result. |
| [Pow(...)](#powref-gpubigintexecutioncontext-in-gpufloatoperand-in-gpufloatoperand-gpubigintmemoryhandle) | Raises a positive base to an arbitrary exponent ($x^y$), rounded toward zero to the result precision. |
| [Reciprocal(...)](#reciprocalref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes the reciprocal ($1 / x$) of an arbitrary-precision floating-point value. |
| [Rsqrt(...)](#rsqrtref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes the reciprocal square root ($1 / \sqrt{x}$) of an arbitrary-precision floating-point value. |
| [Sqrt(...)](#sqrtref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle) | Computes the square root ($\sqrt{x}$) of an arbitrary-precision floating-point value. |

Every method takes its operands as `GpuFloatOperand` (a significand handle, an exponent and a sign: $\pm\text{Significand} \times 2^{\text{Exponent}}$) and writes the significand of the result to `resultSig`. The word count $n$ of `resultSig` sets the precision of the result, $p = 32n$ bits. The method returns the exponent and the sign of the result as a `GpuFloatResult`.

`Normalize`, `Add`, `Multiply`, `Divide`, `Reciprocal`, `Exp`, `Log` and `Pow` return $\mathrm{RZ}_p(v)$: the exact value $v$ rounded toward zero to $p$ bits, with the significand normalized to its top bit. A value that fits in $p$ bits is returned exactly. The result depends only on the values of the operands, not on their word counts or leading zero words, and the GPU and the CPU provider return the same bits. When the exponent of the result does not fit in `int`, they throw `OverflowException`; an intermediate exponent outside `int` (such as the sum of two exponents in `Multiply`) is not an error when the result fits.

## Add(ref GpuBigIntExecutionContext, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)

Adds two arbitrary-precision floating-point operands.

```csharp
GpuFloatResult Add(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand left,
    in GpuFloatOperand right,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`OverflowException`  
The exponent of the result does not fit in `int`.

#### Remarks

The result is $\mathrm{RZ}_p(a + b)$, the exact sum rounded toward zero to $p$ bits, with the significand normalized to its top bit; a sum of zero is positive zero.

## Divide(ref GpuBigIntExecutionContext, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)

Divides one arbitrary-precision floating-point operand by another.

```csharp
GpuFloatResult Divide(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand left,
    in GpuFloatOperand right,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`DivideByZeroException`  
The divisor is zero.

`OverflowException`  
The exponent of the result does not fit in `int`.

#### Remarks

The result is $\mathrm{RZ}_p(a / b)$, whatever the word counts of the operands (a divisor much wider than the result included).

## DivideByUInt32(ref GpuBigIntExecutionContext, in GpuFloatOperand, uint, GpuBigIntMemoryHandle)

Divides an arbitrary-precision floating-point value by a 32-bit unsigned integer scalar.

```csharp
GpuFloatScalarDivResult DivideByUInt32(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    uint divisor,
    GpuBigIntMemoryHandle resultSig);
```

#### Returns

_GpuFloatScalarDivResult_  
The exponent and sign of the quotient and the 32-bit remainder of the significand division.

#### Remarks

This is an integer division of the significand, not a rounding to $p$ bits: the quotient significand is $\lfloor \text{Significand} / \text{divisor} \rfloor$ with the exponent of `value`.

## Exp(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

Computes the natural exponential ($e^x$) of an arbitrary-precision floating-point value.

```csharp
GpuFloatResult Exp(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Returns

_GpuFloatResult_  
The exponent and sign (always positive) of $\mathrm{RZ}_p(e^x)$, the exact value rounded toward zero to $p$ bits. The significand fills `resultSig` with its top bit set; $e^0 = 1$ is $2^{p-1} \times 2^{1-p}$.

#### Exceptions

`OverflowException`  
The exponent of the result does not fit in `int`: an overflow for a large positive argument, an underflow (named in the message) for a large negative one. Every argument with $|x| \ge 2^{32}$ is out of range; the limits are about $\pm 1.4885 \times 10^9$.

#### Remarks

The result is rounded toward zero, like `Add`, `Multiply` and `Divide`, for every argument in range, whatever its size or sign. The GPU and the CPU provider return the same bits. The argument is reduced to $x = k \ln 2 + r$, $r \in (0.69, 1.39)$, for either sign (no reciprocal), and $e^r$ is evaluated as $(e^{r/2^s})^{2^s}$ with a Taylor polynomial in Paterson–Stockmeyer form; [`GpuBigFloatTranscendentalOptions`](GpuBigFloatTranscendentalOptions.md) can fix $s$ and the block size. An approximation with guard bits is accepted only when every value within its error bound truncates to the same $p$ bits; otherwise it is recomputed with more guard bits. That is rare for random arguments; arguments such as $\pm 2^{-c}$ ($c$ above about $p/2$), whose exponential lies very close to a $p$-bit value, take two attempts or more.

## Log(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

Computes the natural logarithm ($\ln x$) of an arbitrary-precision floating-point value.

```csharp
GpuFloatResult Log(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Returns

_GpuFloatResult_  
The exponent and sign of $\mathrm{RZ}_p(\ln x)$. The significand fills `resultSig` with its top bit set, or is zero for $\ln 1 = 0$.

#### Exceptions

`InvalidOperationException`  
`value` is zero or negative.

`OverflowException`  
The exponent of the result does not fit in `int`.

#### Remarks

The result is rounded toward zero for every positive argument, whatever its exponent and however close it is to 1. The GPU and the CPU provider return the same bits. The argument is reduced to $x = m \cdot 2^e$, $m \in [3/4, 3/2)$, and $\ln x = e \ln 2 + 2\,\mathrm{atanh}((m - 1)/(m + 1))$ is evaluated with a number of terms derived from the precision (Horner form for a short series, Paterson–Stockmeyer otherwise). The rounding check retries as for `Exp`; arguments such as $1 \pm 2^{-c}$ take two attempts or more.

## Multiply(ref GpuBigIntExecutionContext, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)

Multiplies two arbitrary-precision floating-point operands.

```csharp
GpuFloatResult Multiply(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand left,
    in GpuFloatOperand right,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`OverflowException`  
The exponent of the result does not fit in `int`. A result whose exponent fits is returned even when the sum of the operand exponents does not.

#### Remarks

The result is $\mathrm{RZ}_p(a \times b)$, the exact product rounded toward zero to $p$ bits.

## Normalize(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

Normalizes an arbitrary-precision floating-point value such that the most significant bit of the significand is aligned to the most significant bit of `resultSig`.

```csharp
GpuFloatResult Normalize(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`OverflowException`  
The exponent of the normalized value (the input exponent minus the shift that moves the top bit to the top of `resultSig`) does not fit in `int`.

#### Remarks

The result is the value rounded toward zero to $p$ bits: a significand of at most $p$ bits is kept exactly, a wider one loses its low bits. Zero gives a zero significand, exponent 0 and a positive sign.

## Pow(ref GpuBigIntExecutionContext, in GpuFloatOperand, in GpuFloatOperand, GpuBigIntMemoryHandle)

Raises an arbitrary-precision floating-point base to an arbitrary-precision floating-point exponent ($x^y$).

```csharp
GpuFloatResult Pow(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand baseValue,
    in GpuFloatOperand exponentValue,
    GpuBigIntMemoryHandle resultSig);
```

#### Returns

_GpuFloatResult_  
The exponent and sign (always positive) of $\mathrm{RZ}_p(x^y)$. The significand fills `resultSig` with its top bit set.

#### Exceptions

`InvalidOperationException`  
`baseValue` is zero or negative, whatever the exponent (also $y = 0$).

`OverflowException`  
The exponent of the result does not fit in `int`: "Pow overflow" for a result above 1, "Pow underflow" for one below 1.

#### Remarks

The result is rounded toward zero for every positive base, and the GPU and the CPU provider return the same bits. A power that is exactly a $p$-bit value is found first and returned as it is: $x^0 = 1$, $1^y = 1$, $2^3 = 8$, $2^{1000}$, $4^{0.5} = 2$, $(9/4)^{0.5} = 3/2$, $2^{-3} = 1/8$, and every other $x^y$ that is a dyadic value of at most $p$ significant bits (the check of MPFR's `mpfr_pow_is_exact`, for an integer $y$ and for $y = n / 2^k$). Every other power is computed as $e^{y \cdot L}$ with $L \approx \ln x$ to the Exp precision plus 64 bits, and the rounding check covers the error that $L$ carries into $y \cdot L$; it retries as for `Exp` and always ends, because such a power is never a $p$-bit value. Powers next to a $p$-bit value (for example $(2 + 2^{-200})^3$ at $p = 128$) take two attempts or more.

## Reciprocal(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

Computes the reciprocal ($1 / x$) of an arbitrary-precision floating-point value.

```csharp
GpuFloatResult Reciprocal(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`DivideByZeroException`  
`value` is zero.

`OverflowException`  
The exponent of the result does not fit in `int` (the reciprocal of a value with an exponent near `int.MaxValue`).

#### Remarks

The result is $\mathrm{RZ}_p(1 / x)$, whatever the word count of `value` and its leading zero words; a significand of 1 gives the exact power of two.

## Rsqrt(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

Computes the reciprocal square root ($1 / \sqrt{x}$) of an arbitrary-precision floating-point value.

```csharp
GpuFloatResult Rsqrt(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`InvalidOperationException`  
`value` is negative.

## Sqrt(ref GpuBigIntExecutionContext, in GpuFloatOperand, GpuBigIntMemoryHandle)

Computes the square root ($\sqrt{x}$) of an arbitrary-precision floating-point value.

```csharp
GpuFloatResult Sqrt(
    ref GpuBigIntExecutionContext context,
    in GpuFloatOperand value,
    GpuBigIntMemoryHandle resultSig);
```

#### Exceptions

`InvalidOperationException`  
`value` is negative.

## Remarks

This interface abstracts low-level mantissa and exponent transformations for [GpuBigFloat](GpuBigFloat.md) and [GpuBigUFloat](GpuBigUFloat.md) representations, operating on structured operands (`GpuFloatOperand`) and raw memory handles (`GpuBigIntMemoryHandle`) via a [GpuBigIntExecutionContext](../gpu_big_int_and_uint/GpuBigIntExecutionContext.md).

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
- [GpuBigFloatTranscendentalOptions Class](GpuBigFloatTranscendentalOptions.md)
- [FloatExpressions](../gpu_big_int_and_uint/expressions/FloatExpressions.md)
- [GpuBigIntExecutionContext Struct](../gpu_big_int_and_uint/GpuBigIntExecutionContext.md)
