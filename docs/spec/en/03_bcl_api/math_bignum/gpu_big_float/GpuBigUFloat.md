---
title: "GpuBigUFloat Struct (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Reference for arbitrary-precision unsigned floating-point numbers on the GPU."
---

## GpuBigUFloat Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents an arbitrary-precision unsigned floating-point number on the GPU using a dynamic-size significand and separate exponent.

```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigUFloat : IEquatable<GpuBigUFloat>, IComparable<GpuBigUFloat>, IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigUFloat(GpuBigUInt, int)](#gpubigufloatgpubiguint-int) | Initializes a new instance of the `GpuBigUFloat` struct with the specified significand and exponent. |

## GpuBigUFloat(GpuBigUInt, int)

Initializes a new instance of the `GpuBigUFloat` struct with the specified significand and exponent.

```csharp
public GpuBigUFloat([TakesOwnership] GpuBigUInt significand, int exponent);
```

#### Parameters

`significand` _GpuBigUInt_<br>
The significand payload. Takes ownership of the GPU buffer.

`exponent` _int_<br>
The binary base-2 exponent.

## Properties

| Name | Description |
| --- | --- |
| [Exponent](#exponent) | Gets the binary base-2 exponent of the floating-point number. |
| [Significand](#significand) | Gets the significand (mantissa) stored as a dynamic-size unsigned big integer in GPU device memory. |

## Exponent

Gets the binary base-2 exponent of the floating-point number.

```csharp
public int Exponent { get; }
```

## Significand

Gets the significand (mantissa) stored as a dynamic-size unsigned big integer in GPU device memory.

```csharp
public GpuBigUInt Significand { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigUFloat)](#addgpubigufloat) | Adds another `GpuBigUFloat` to this instance. |
| [AlignRadixPoint(GpuBigUFloat)](#alignradixpointgpubigufloat) | Aligns the radix points (exponents) of this instance and another unsigned floating-point number, extracting IEEE 754 rounding bits. |
| [CompareTo(GpuBigUFloat)](#comparetogpubigufloat) | Compares this instance to another `GpuBigUFloat` and returns an integer indicating their relative order. |
| [Dispose()](#dispose) | Releases the GPU global device memory resources allocated for the underlying `Significand`. |
| [DivideByUInt32(uint)](#dividebyuint32uint) | Divides the unsigned floating-point number by a 32-bit unsigned integer scalar. |
| [Equals(GpuBigUFloat)](#equalsgpubigufloat) | Indicates whether the current instance is equal to another `GpuBigUFloat` instance. |
| [Equals(object?)](#equalsobject) | Determines whether the specified object is equal to the current instance. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |
| [Normalize(int, bool)](#normalizeint-bool) | Normalizes the unsigned floating-point value by shifting significand bits and adjusting the exponent. |
| [Round(GpuBigFloatRoundingMode, bool, bool, bool)](#roundgpubigfloatroundingmode-bool-bool-bool) | Rounds the floating-point value to the target significand precision using the specified rounding mode and discarded bit flags. |
| [Subtract(GpuBigUFloat)](#subtractgpubigufloat) | Subtracts another `GpuBigUFloat` from this instance. |

## Add(GpuBigUFloat)

Adds another `GpuBigUFloat` to this instance.

```csharp
public GpuBigUFloat Add(GpuBigUFloat other);
```

#### Parameters

`other` _GpuBigUFloat_<br>
The value to add.

#### Returns

_GpuBigUFloat_<br>
The sum of this instance and `other`.

## AlignRadixPoint(GpuBigUFloat)

Aligns the radix points (exponents) of this instance and another unsigned floating-point number, extracting IEEE 754 rounding bits.

```csharp
public (GpuBigUFloat AlignedThis, GpuBigUFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther) AlignRadixPoint(GpuBigUFloat other);
```

#### Parameters

`other` _GpuBigUFloat_<br>
The other unsigned floating-point instance to align with.

#### Returns

_(GpuBigUFloat AlignedThis, GpuBigUFloat AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther)_<br>
A tuple containing the aligned values and IEEE 754 guard, round, and sticky bits.

## CompareTo(GpuBigUFloat)

Compares this instance to another `GpuBigUFloat` and returns an integer indicating their relative order.

```csharp
public int CompareTo(GpuBigUFloat other);
```

#### Parameters

`other` _GpuBigUFloat_<br>
The `GpuBigUFloat` to compare with this instance.

#### Returns

_int_<br>
A signed integer that indicates the relative values of this instance and `other`.

## Dispose()

Releases the GPU global device memory resources allocated for the underlying `Significand`.

```csharp
public void Dispose();
```

## DivideByUInt32(uint)

Divides the unsigned floating-point number by a 32-bit unsigned integer scalar.

```csharp
public (GpuBigUFloat Quotient, uint Remainder) DivideByUInt32(uint divisor);
```

#### Parameters

`divisor` _uint_<br>
The 32-bit unsigned integer divisor.

#### Returns

_(GpuBigUFloat Quotient, uint Remainder)_<br>
A tuple containing the quotient `GpuBigUFloat` and the 32-bit integer remainder.

## Equals(GpuBigUFloat)

Indicates whether the current instance is equal to another `GpuBigUFloat` instance.

```csharp
public bool Equals(GpuBigUFloat other);
```

#### Parameters

`other` _GpuBigUFloat_<br>
A `GpuBigUFloat` to compare with this instance.

#### Returns

_bool_<br>
`true` if the current instance is equal to the `other` parameter; otherwise, `false`.

## Equals(object?)

Determines whether the specified object is equal to the current instance.

```csharp
public override bool Equals(object? obj);
```

#### Parameters

`obj` _object?_<br>
The object to compare with the current instance.

#### Returns

_bool_<br>
`true` if `obj` is a `GpuBigUFloat` and equals the current instance; otherwise, `false`.

## GetHashCode()

Returns the hash code for this instance.

```csharp
public override int GetHashCode();
```

#### Returns

_int_<br>
A 32-bit signed integer hash code.

## Normalize(int, bool)

Normalizes the unsigned floating-point value by shifting significand bits and adjusting the exponent.

```csharp
public GpuBigUFloat Normalize(int leadingZeros = 0, bool carryOut = false);
```

#### Parameters

`leadingZeros` _int_<br>
The number of leading zero bits in the significand.

`carryOut` _bool_<br>
`true` if an arithmetic carry out occurred during addition; otherwise, `false`.

#### Returns

_GpuBigUFloat_<br>
The normalized `GpuBigUFloat` instance.

## Round(GpuBigFloatRoundingMode, bool, bool, bool)

Rounds the floating-point value to the target significand precision using the specified rounding mode and discarded bit flags.

```csharp
public GpuBigUFloat Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit);
```

#### Parameters

`mode` _GpuBigFloatRoundingMode_<br>
The IEEE 754 rounding mode to apply.

`guardBit` _bool_<br>
The first discarded bit beyond the target precision.

`roundBit` _bool_<br>
The second discarded bit beyond the target precision.

`stickyBit` _bool_<br>
`true` if any remaining discarded bits beyond `roundBit` were non-zero.

#### Returns

_GpuBigUFloat_<br>
The rounded `GpuBigUFloat` instance.

## Subtract(GpuBigUFloat)

Subtracts another `GpuBigUFloat` from this instance.

```csharp
public GpuBigUFloat Subtract(GpuBigUFloat other);
```

#### Parameters

`other` _GpuBigUFloat_<br>
The value to subtract.

#### Returns

_GpuBigUFloat_<br>
The difference of this instance minus `other`.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuBigUFloat, GpuBigUFloat)` | Creates a deferred `BinaryExpr<GpuBigUFloat, AddOp>` expression representing the addition of two unsigned floating-point values. |
| `Subtraction(GpuBigUFloat, GpuBigUFloat)` | Creates a deferred `BinaryExpr<GpuBigUFloat, SubOp>` expression representing the subtraction of two unsigned floating-point values. |
| `Implicit(BinaryExpr<GpuBigUFloat, AddOp> to GpuBigUFloat)` | Implicitly evaluates and converts an addition expression to an evaluated `GpuBigUFloat` instance. |
| `Implicit(BinaryExpr<GpuBigUFloat, SubOp> to GpuBigUFloat)` | Implicitly evaluates and converts a subtraction expression to an evaluated `GpuBigUFloat` instance. |
| `Equality(GpuBigUFloat, GpuBigUFloat)` | Determines whether two specified `GpuBigUFloat` instances are equal. |
| `Inequality(GpuBigUFloat, GpuBigUFloat)` | Determines whether two specified `GpuBigUFloat` instances are not equal. |
| `LessThan(GpuBigUFloat, GpuBigUFloat)` | Determines whether one specified `GpuBigUFloat` is less than another specified `GpuBigUFloat`. |
| `GreaterThan(GpuBigUFloat, GpuBigUFloat)` | Determines whether one specified `GpuBigUFloat` is greater than another specified `GpuBigUFloat`. |
| `LessThanOrEqual(GpuBigUFloat, GpuBigUFloat)` | Determines whether one specified `GpuBigUFloat` is less than or equal to another specified `GpuBigUFloat`. |
| `GreaterThanOrEqual(GpuBigUFloat, GpuBigUFloat)` | Determines whether one specified `GpuBigUFloat` is greater than or equal to another specified `GpuBigUFloat`. |

## Examples

### Unsigned Float Operations

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

// 100 * 2^0 = 100
var sigA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(100), minWords: 4);
using var uFloatA = new GpuBigUFloat(sigA, exponent: 0);

// 50 * 2^-1 = 25
var sigB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(50), minWords: 4);
using var uFloatB = new GpuBigUFloat(sigB, exponent: -1);

// Unsigned addition via deferred expression evaluation (100 + 25 = 125)
using GpuBigUFloat sum = uFloatA + uFloatB;

Console.WriteLine($"Computed UFloat Sum: Significand={sum.Significand.ToBigInteger()}, Exponent={sum.Exponent}");
```

## Remarks

This struct represents an arbitrary-precision unsigned floating-point value $\text{Significand} \times 2^{\text{Exponent}}$ backed by GPU device memory handles (`GpuBigUInt`). It implements `IDisposable` to deterministically return the significand buffer to the `GpuWorkspace` memory pool.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigFloat Struct (G#)](GpuBigFloat.md)
- [GpuBigFloatRoundingMode Enum (G#)](GpuBigFloatRoundingMode.md)
