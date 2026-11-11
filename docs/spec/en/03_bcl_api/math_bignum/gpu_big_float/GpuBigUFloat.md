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
public readonly struct GpuBigUFloat : IGpuBigNumber<GpuBigUFloat>
```

Implements: [`IGpuBigNumber<GpuBigUFloat>`](../gpu_big_int_and_uint/00_index.md#numeric-interfaces), `IGpuNumber<GpuBigUFloat>`, `IGpuBigNumberBase`, `IEquatable<GpuBigUFloat>`, `IComparable<GpuBigUFloat>`, `IDisposable`

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
| [CanBeNegative](#canbenegative) | Always `false`: `GpuBigUFloat` is an unsigned type. |
| [EffectiveScale](#effectivescale) | Gets the effective binary scale (`Exponent + GetBitLength()`) of a non-zero value. |
| [Exponent](#exponent) | Gets the binary base-2 exponent of the floating-point number. |
| [IsNegative](#isnegative) | Always `false`. |
| [Magnitude](#magnitude) | Gets the significand buffer (the same value as `Significand`). |
| [Significand](#significand) | Gets the significand (mantissa) stored as a dynamic-size unsigned big integer in GPU device memory. |
| [Workspace](#workspace) | Gets the `GpuWorkspace` that owns the significand buffer. |

`IGpuBigNumber<GpuBigUFloat>.AdditionIsExact` is implemented explicitly and returns `false`: every addition rounds, so a sum adds from the left in the order written.

## CanBeNegative

Always `false`: `GpuBigUFloat` is an unsigned type.

```csharp
public bool CanBeNegative { get; }
```

## EffectiveScale

Gets the effective binary scale (`Exponent + GetBitLength()`) of a non-zero value.

```csharp
public long EffectiveScale { get; }
```


## Exponent

Gets the binary base-2 exponent of the floating-point number.

```csharp
public int Exponent { get; }
```

## IsNegative

Always `false`.

```csharp
public bool IsNegative { get; }
```

## Magnitude

Gets the significand buffer (the same value as `Significand`).

```csharp
public GpuBigUInt Magnitude { get; }
```

## Significand

Gets the significand (mantissa) stored as a dynamic-size unsigned big integer in GPU device memory.

```csharp
public GpuBigUInt Significand { get; }
```

## Workspace

Gets the `GpuWorkspace` that owns the significand buffer.

```csharp
public GpuWorkspace Workspace { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigUFloat)](#addgpubigufloat) | Adds another `GpuBigUFloat` to this instance. |
| [CompareTo(GpuBigUFloat)](#comparetogpubigufloat) | Compares this instance to another `GpuBigUFloat` and returns an integer indicating their relative order. |
| [Create(GpuBigUInt, int, bool)](#creategpubiguint-int-bool) | Creates a `GpuBigUFloat` from a magnitude and an exponent; throws `ArgumentOutOfRangeException` for a negative non-zero value. |
| [Divide(GpuBigUFloat)](#dividegpubigufloat) | Divides this instance by another `GpuBigUFloat` (`this / other`). |
| [Dispose()](#dispose) | Releases the GPU global device memory resources allocated for the underlying `Significand`. |
| [DivideByUInt32(uint)](#dividebyuint32uint) | Divides the unsigned floating-point number by a 32-bit unsigned integer scalar. |
| [Equals(GpuBigUFloat)](#equalsgpubigufloat) | Indicates whether the current instance has the same significand buffer and exponent as another `GpuBigUFloat`. |
| [Equals(object?)](#equalsobject) | Determines whether the specified object is equal to the current instance. |
| [GetBitLength()](#getbitlength) | Returns the bit length of the significand, or 0 for zero. |
| [GetHashCode()](#gethashcode) | Returns the hash code of the significand buffer and the exponent, consistent with `Equals(GpuBigUFloat)`. |
| [IsZero()](#iszero) | Determines whether the value is zero. |
| [Multiply(GpuBigUFloat)](#multiplygpubigufloat) | Multiplies this instance by another `GpuBigUFloat` (`this * other`). |
| [Normalize(int, bool)](#normalizeint-bool) | Normalizes the unsigned floating-point value by shifting significand bits and adjusting the exponent. |
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

## Create(GpuBigUInt, int, bool)

Creates a `GpuBigUFloat` from a magnitude and an exponent. `isNegative` must be `false` unless the magnitude is zero; otherwise `ArgumentOutOfRangeException` is thrown.

```csharp
public static GpuBigUFloat Create([TakesOwnership] GpuBigUInt magnitude, int exponent, bool isNegative);
```

## Divide(GpuBigUFloat)

Divides this instance by another `GpuBigUFloat` (`this / other`).

```csharp
public GpuBigUFloat Divide(GpuBigUFloat other);
```

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

Indicates whether the current instance has the same significand buffer and exponent as another `GpuBigUFloat`.

```csharp
public bool Equals(GpuBigUFloat other);
```

#### Parameters

`other` _GpuBigUFloat_<br>
A `GpuBigUFloat` to compare with this instance.

#### Returns

_bool_<br>
`true` if `Significand` refers to the same buffer and `Exponent` is the same; otherwise, `false`.

#### Remarks

Like the integer types, `Equals` and `GetHashCode` compare identity, not values: two separate buffers holding the same value, or the same value written with another exponent (`12 * 2^0` and `3 * 2^2`), are not equal. Use `==` or `CompareTo` to compare values on the GPU. Neither member reads device memory, so both also work on a disposed value.

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

Returns the hash code of the significand buffer and the exponent, consistent with `Equals(GpuBigUFloat)`.

```csharp
public override int GetHashCode();
```

#### Returns

_int_<br>
A 32-bit signed integer hash code.

## GetBitLength()

Returns the bit length of the significand, or 0 for zero.

```csharp
public int GetBitLength();
```

## IsZero()

Determines whether the value is zero.

```csharp
public bool IsZero();
```

## Multiply(GpuBigUFloat)

Multiplies this instance by another `GpuBigUFloat` (`this * other`).

```csharp
public GpuBigUFloat Multiply(GpuBigUFloat other);
```

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

#### Exceptions

`OverflowException`  
Thrown when `other` is greater than this instance: an unsigned value has no negative difference, as with a `checked` `uint` subtraction.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuBigUFloat, GpuBigUFloat)` | Creates a deferred `BinaryExpr<GpuBigUFloat, AddOp>` expression representing the addition of two unsigned floating-point values. |
| `Subtraction(GpuBigUFloat, GpuBigUFloat)` | Creates a deferred `BinaryExpr<GpuBigUFloat, SubOp>` expression representing the subtraction of two unsigned floating-point values. |
| `Multiply(GpuBigUFloat, GpuBigUFloat)` | Creates a deferred `BinaryExpr<GpuBigUFloat, MulOp>` expression representing the multiplication of two unsigned floating-point values. |
| `Division(GpuBigUFloat, GpuBigUFloat)` | Creates a deferred `BinaryExpr<GpuBigUFloat, DivOp>` expression representing the division of two unsigned floating-point values. |
| `Implicit(BinaryExpr<GpuBigUFloat, AddOp> to GpuBigUFloat)` | Implicitly evaluates and converts an addition expression to an evaluated `GpuBigUFloat` instance. |
| `Implicit(BinaryExpr<GpuBigUFloat, SubOp> to GpuBigUFloat)` | Implicitly evaluates and converts a subtraction expression to an evaluated `GpuBigUFloat` instance. Throws `OverflowException` when the subtrahend is greater than the minuend. |
| `Implicit(BinaryExpr<GpuBigUFloat, MulOp> to GpuBigUFloat)` | Implicitly evaluates and converts a multiplication expression to an evaluated `GpuBigUFloat` instance. |
| `Implicit(BinaryExpr<GpuBigUFloat, DivOp> to GpuBigUFloat)` | Implicitly evaluates and converts a division expression to an evaluated `GpuBigUFloat` instance. |
| `Equality(GpuBigUFloat, GpuBigUFloat)` | Determines whether two specified `GpuBigUFloat` instances have the same value on the GPU. |
| `Inequality(GpuBigUFloat, GpuBigUFloat)` | Determines whether two specified `GpuBigUFloat` instances have different values on the GPU. |
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
