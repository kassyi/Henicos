---
title: "GpuBigFloat Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents an arbitrary-precision signed floating-point number on the GPU using an unsigned floating-point magnitude and a sign flag."
---

## GpuBigFloat Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents an arbitrary-precision signed floating-point number on the GPU using a dynamically sized unsigned floating-point magnitude ([`GpuBigUFloat`](GpuBigUFloat.md)) and a sign flag:

$$(-1)^{\text{IsNegative}} \times \text{UFloat.Significand} \times 2^{\text{UFloat.Exponent}}$$

```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigFloat : IEquatable<GpuBigFloat>, IComparable<GpuBigFloat>, IDisposable
```

Implements: `IEquatable<GpuBigFloat>`, `IComparable<GpuBigFloat>`, `IDisposable`

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigFloat(GpuBigUFloat, Boolean)](#gpubigfloatgpubigufloat-boolean) | Initializes a new instance of the `GpuBigFloat` struct from an unsigned float magnitude and a sign flag. |
| [GpuBigFloat(GpuBigUInt, Int32, Boolean)](#gpubigfloatgpubiguint-int32-boolean) | Initializes a new instance of the `GpuBigFloat` struct from a significand, binary base-2 exponent, and sign flag. |

## GpuBigFloat(GpuBigUFloat, Boolean)

Initializes a new instance of the `GpuBigFloat` struct from an unsigned float magnitude and a sign flag.

```csharp
public GpuBigFloat([TakesOwnership] GpuBigUFloat uFloat, bool isNegative);
```

#### Parameters

`uFloat` _[GpuBigUFloat](GpuBigUFloat.md)_<br>
The unsigned float component. Takes ownership of the underlying buffer.

`isNegative` _Boolean_<br>
`true` if the value is negative; otherwise, `false`.

## GpuBigFloat(GpuBigUInt, Int32, Boolean)

Initializes a new instance of the `GpuBigFloat` struct from significand, binary exponent, and sign flag.

```csharp
public GpuBigFloat([TakesOwnership] GpuBigUInt significand, int exponent, bool isNegative);
```

#### Parameters

`significand` _[GpuBigUInt](../gpu_big_int_and_uint/GpuBigUInt.md)_<br>
The significand payload. Takes ownership of the buffer.

`exponent` _Int32_<br>
The binary base-2 exponent.

`isNegative` _Boolean_<br>
`true` if the value is negative; otherwise, `false`.

## Properties

| Name | Description |
| --- | --- |
| [IsNegative](#isnegative) | Gets a value indicating whether the floating-point number is negative. |
| [UFloat](#ufloat) | Gets the underlying unsigned floating-point magnitude payload (`GpuBigUFloat`). |

## IsNegative

Gets a value indicating whether the floating-point number is negative.

```csharp
public bool IsNegative { get; }
```

## UFloat

Gets the underlying unsigned floating-point magnitude payload.

```csharp
public GpuBigUFloat UFloat { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigFloat)](#addgpubigfloat) | Adds another `GpuBigFloat` to this instance. |
| [AlignRadixPoint(GpuBigFloat)](#alignradixpointgpubigfloat) | Aligns the radix points (exponents) of this instance and another floating-point number, extracting IEEE 754 rounding bits. |
| [CompareTo(GpuBigFloat)](#comparetogpubigfloat) | Compares this instance to another `GpuBigFloat` and returns an integer indicating their relative order. |
| [Dispose()](#dispose) | Releases the GPU global device memory resources allocated for the underlying significand. |
| [DivideByUInt32(UInt32)](#dividebyuint32uint32) | Divides the floating-point number by a 32-bit unsigned integer scalar, returning the quotient and 32-bit remainder. |
| [Equals(GpuBigFloat)](#equalsgpubigfloat) | Indicates whether the current instance is equal to another `GpuBigFloat` instance. |
| [Equals(Object?)](#equalsobject) | Determines whether the specified object is equal to the current instance. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |
| [Normalize(Int32, Boolean)](#normalizeint32-boolean) | Normalizes the floating-point value such that the most significant bit of the significand is aligned. |
| [Round(GpuBigFloatRoundingMode, Boolean, Boolean, Boolean)](#roundgpubigfloatroundingmode-boolean-boolean-boolean) | Rounds the floating-point value to the target significand precision using the specified rounding mode and discarded bit flags. |
| [Subtract(GpuBigFloat)](#subtractgpubigfloat) | Subtracts another `GpuBigFloat` from this instance. |

## Methods Detail

### Add(GpuBigFloat)

Adds another `GpuBigFloat` to this instance.

```csharp
public GpuBigFloat Add(GpuBigFloat other);
```

### AlignRadixPoint(GpuBigFloat)

Aligns the radix points (exponents) of this instance and another floating-point number, extracting IEEE 754 rounding bits.

```csharp
public (GpuBigFloat AlignedThis, GpuBigFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther) AlignRadixPoint(GpuBigFloat other);
```

### CompareTo(GpuBigFloat)

Compares this instance to another `GpuBigFloat` and returns an integer indicating their relative order.

```csharp
public int CompareTo(GpuBigFloat other);
```

### Dispose()

Releases the GPU global device memory resources allocated for the underlying significand.

```csharp
public void Dispose();
```

### DivideByUInt32(UInt32)

Divides the floating-point number by a 32-bit unsigned integer scalar.

```csharp
public (GpuBigFloat Quotient, uint Remainder) DivideByUInt32(uint divisor);
```

#### Parameters

`divisor` _UInt32_<br>
The 32-bit unsigned integer divisor.

#### Returns

_(`GpuBigFloat` Quotient, `UInt32` Remainder)_<br>
A tuple containing the quotient `GpuBigFloat` and the 32-bit integer remainder.

### Equals(GpuBigFloat)

Indicates whether the current instance is equal to another `GpuBigFloat` instance.

```csharp
public bool Equals(GpuBigFloat other);
```

### Equals(Object?)

Determines whether the specified object is equal to the current instance.

```csharp
public override bool Equals(object? obj);
```

### GetHashCode()

Returns the hash code for this instance.

```csharp
public override int GetHashCode();
```

### Normalize(Int32, Boolean)

Normalizes the floating-point value such that the most significant bit of the significand is aligned.

```csharp
public GpuBigFloat Normalize(int leadingZeros = 0, bool carryOut = false);
```

#### Parameters

`leadingZeros` _Int32_<br>
The number of leading zero bits in the significand.

`carryOut` _Boolean_<br>
`true` if an arithmetic carry out occurred during addition; otherwise, `false`.

#### Returns

_[GpuBigFloat](GpuBigFloat.md)_<br>
The normalized `GpuBigFloat` instance.

### Round(GpuBigFloatRoundingMode, Boolean, Boolean, Boolean)

Rounds the floating-point value to the target significand precision using the specified rounding mode and discarded bit flags.

```csharp
public GpuBigFloat Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit);
```

### Subtract(GpuBigFloat)

Subtracts another `GpuBigFloat` from this instance.

```csharp
public GpuBigFloat Subtract(GpuBigFloat other);
```

## Operators

| Name | Description |
| --- | --- |
| `operator +(GpuBigFloat, GpuBigFloat)` | Creates a deferred `BinaryExpr<GpuBigFloat, AddOp>` representing the addition of two floating-point values. |
| `operator -(GpuBigFloat, GpuBigFloat)` | Creates a deferred `BinaryExpr<GpuBigFloat, SubOp>` representing the subtraction of two floating-point values. |
| `operator *(GpuBigFloat, GpuBigFloat)` | Creates a deferred `BinaryExpr<GpuBigFloat, MulOp>` representing the multiplication of two floating-point values. |
| `implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, AddOp>)` | Implicitly evaluates a binary addition expression via `GpuBigFloatExpressionVisitor`. |
| `implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, SubOp>)` | Implicitly evaluates a binary subtraction expression via `GpuBigFloatExpressionVisitor`. |
| `implicit operator GpuBigFloat(BinaryExpr<GpuBigFloat, MulOp>)` | Implicitly evaluates a binary multiplication expression via `GpuBigFloatExpressionVisitor`. |
| `operator ==(GpuBigFloat, GpuBigFloat)` | Determines whether two specified `GpuBigFloat` instances are equal. |
| `operator !=(GpuBigFloat, GpuBigFloat)` | Determines whether two specified `GpuBigFloat` instances are not equal. |
| `operator <(GpuBigFloat, GpuBigFloat)` | Determines whether one specified `GpuBigFloat` is less than another. |
| `operator >(GpuBigFloat, GpuBigFloat)` | Determines whether one specified `GpuBigFloat` is greater than another. |
| `operator <=(GpuBigFloat, GpuBigFloat)` | Determines whether one specified `GpuBigFloat` is less than or equal to another. |
| `operator >=(GpuBigFloat, GpuBigFloat)` | Determines whether one specified `GpuBigFloat` is greater than or equal to another. |

## Examples

### Arbitrary-Precision Floating-Point Arithmetic on GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

// Create 3.0 (3 * 2^0) and 1.5 (3 * 2^-1)
using var sigA = new GpuBigUInt(workspace, new uint[] { 3, 0 });
using var sigB = new GpuBigUInt(workspace, new uint[] { 3, 0 });

var floatA = new GpuBigFloat(sigA, exponent: 0, isNegative: false);
var floatB = new GpuBigFloat(sigB, exponent: -1, isNegative: false);

// Addition via deferred BinaryExpr<GpuBigFloat, AddOp> and implicit evaluation
using GpuBigFloat sum = floatA + floatB;

// Scalar division by UInt32
var (quotient, remainder) = floatA.DivideByUInt32(2);
using var q = quotient;

Console.WriteLine($"Sum: Significand={sum.UFloat.Significand.ToBigInteger(false)}, Exp={sum.UFloat.Exponent}, Neg={sum.IsNegative}");
Console.WriteLine($"Quotient: Significand={q.UFloat.Significand.ToBigInteger(false)}, Exp={q.UFloat.Exponent}, Rem={remainder}");
```

## Remarks

`GpuBigFloat` is backed by dynamically sized device memory allocations in [`GpuBigUInt`](../gpu_big_int_and_uint/GpuBigUInt.md) with zero managed heap overhead on the host. Arithmetic operators (`+`, `-`, `*`) construct stack-allocated [`BinaryExpr<GpuBigFloat, TOp>`](../gpu_big_int_and_uint/expressions/FloatExpressions.md) nodes that are evaluated through [`GpuBigFloatExpressionVisitor`](../gpu_big_int_and_uint/expressions/FloatExpressions.md).

For more information on the separation of Signed and Unsigned components to prevent Warp Divergence, see [The Architecture: Signed vs. Unsigned Separation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md#3-the-architecture-signed-vs-unsigned-separation).

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigUFloat Struct (G#)](GpuBigUFloat.md)
- [GpuBigFloatRoundingMode Enum (G#)](GpuBigFloatRoundingMode.md)
- [FloatExpressions (BinaryExpr & GpuBigFloatExpressionVisitor)](../gpu_big_int_and_uint/expressions/FloatExpressions.md)
- [GpuBigFloat Operators (G#)](Operators.md)
