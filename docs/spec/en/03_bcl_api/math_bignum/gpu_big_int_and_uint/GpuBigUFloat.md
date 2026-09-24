---
title: "GpuBigUFloat<TSize> Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents an arbitrary-precision unsigned floating-point number on the GPU using a fixed-size significand and separate exponent."
---

## GpuBigUFloat&lt;TSize&gt; Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigFloat`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents an arbitrary-precision unsigned floating-point number on the GPU using a fixed-size significand and separate exponent.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigUFloat<TSize> : IEquatable<GpuBigUFloat<TSize>>, IComparable<GpuBigUFloat<TSize>> where TSize : unmanaged
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigUFloat&lt;TSize&gt;(GpuUIntBigInteger&lt;TSize&gt;, int)](#gpubigufloatt-sizegpuuintbigintegert-size-int) | Initializes a new instance of the `GpuBigUFloat<TSize>` struct. |

## GpuBigUFloat&lt;TSize&gt;(GpuUIntBigInteger&lt;TSize&gt;, int)

Initializes a new instance of the `GpuBigUFloat<TSize>` struct.

```csharp
public GpuBigUFloat(GpuUIntBigInteger<TSize> significand, int exponent);
```

#### Parameters

`significand` _GpuUIntBigInteger&lt;TSize&gt;_<br>
The significand payload.

`exponent` _int_<br>
The binary exponent.

## Properties

| Name | Description |
| --- | --- |
| [Exponent](#exponent) | The binary exponent of the floating-point number. |
| [Significand](#significand) | The significand (mantissa) stored as a fixed-size unsigned big integer. |

## Exponent

The binary exponent of the floating-point number.

```csharp
public int Exponent;
```

## Significand

The significand (mantissa) stored as a fixed-size unsigned big integer.

```csharp
public GpuUIntBigInteger<TSize> Significand;
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigUFloat&lt;TSize&gt;)](#addgpubigufloatt-size) | Adds two unsigned floating-point numbers. |
| [AlignRadixPoint(GpuBigUFloat&lt;TSize&gt;, out GpuBigUFloat&lt;TSize&gt;, out GpuBigUFloat&lt;TSize&gt;, out bool, out bool)](#alignradixpointgpubigufloatt-size-out-gpubigufloatt-size-out-gpubigufloatt-size-out-bool-out-bool) | Aligns the radix point of this instance and another instance. |
| [CompareTo(GpuBigUFloat&lt;TSize&gt;)](#comparetogpubigufloatt-size) | Compares this instance to another unsigned floating-point number. |
| [Equals(GpuBigUFloat&lt;TSize&gt;)](#equalsgpubigufloatt-size) | Returns a value indicating whether this instance is equal to another. |
| [Equals(object?)](#equalsobject) | Returns a value indicating whether this instance is equal to a specified object. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |
| [Normalize(int, bool)](#normalizeint-bool) | Normalizes the floating-point significand and adjusts the exponent. |
| [Round(GpuBigFloatRoundingMode, bool, bool, bool)](#roundgpubigfloatroundingmode-bool-bool-bool) | Applies IEEE 754 compliant rounding to the floating-point value. |
| [Subtract(GpuBigUFloat&lt;TSize&gt;)](#subtractgpubigufloatt-size) | Subtracts an unsigned floating-point number from this instance. |

## Add(GpuBigUFloat&lt;TSize&gt;)

Adds two unsigned floating-point numbers.

```csharp
public readonly GpuBigUFloat<TSize> Add(GpuBigUFloat<TSize> other);
```

#### Parameters

`other` _GpuBigUFloat&lt;TSize&gt;_<br>
The value to add.

#### Returns

_GpuBigUFloat&lt;TSize&gt;_<br>
The sum of the two values.

## AlignRadixPoint(GpuBigUFloat&lt;TSize&gt;, out GpuBigUFloat&lt;TSize&gt;, out GpuBigUFloat&lt;TSize&gt;, out bool, out bool)

Aligns the radix point of this instance and another instance.

```csharp
public readonly void AlignRadixPoint(GpuBigUFloat<TSize> other, out GpuBigUFloat<TSize> alignedThis, out GpuBigUFloat<TSize> alignedOther, out bool stickyThis, out bool stickyOther);
```

#### Parameters

`other` _GpuBigUFloat&lt;TSize&gt;_<br>
The operand to align against.

`alignedThis` _out GpuBigUFloat&lt;TSize&gt;_<br>
The resulting aligned this operand.

`alignedOther` _out GpuBigUFloat&lt;TSize&gt;_<br>
The resulting aligned other operand.

`stickyThis` _out bool_<br>
The sticky bit preserved from right-shifting this operand.

`stickyOther` _out bool_<br>
The sticky bit preserved from right-shifting the other operand.

## CompareTo(GpuBigUFloat&lt;TSize&gt;)

Compares this instance to another unsigned floating-point number.

```csharp
public readonly int CompareTo(GpuBigUFloat<TSize> other);
```

#### Parameters

`other` _GpuBigUFloat&lt;TSize&gt;_<br>
The value to compare to.

#### Returns

_int_<br>
A signed integer that indicates the relative order of the values being compared.

## Equals(GpuBigUFloat&lt;TSize&gt;)

Returns a value indicating whether this instance is equal to another.

```csharp
public readonly bool Equals(GpuBigUFloat<TSize> other);
```

#### Parameters

`other` _GpuBigUFloat&lt;TSize&gt;_<br>
The value to compare.

#### Returns

_bool_<br>
`true` if the values are equal; otherwise, `false`.

## Equals(object?)

Returns a value indicating whether this instance is equal to a specified object.

```csharp
public readonly override bool Equals(object? obj);
```

#### Parameters

`obj` _object?_<br>
The object to compare.

#### Returns

_bool_<br>
`true` if `obj` is equal to this instance; otherwise, `false`.

## GetHashCode()

Returns the hash code for this instance.

```csharp
public readonly override int GetHashCode();
```

#### Returns

_int_<br>
A 32-bit signed integer that is the hash code for this instance.

## Normalize(int, bool)

Normalizes the floating-point significand and adjusts the exponent.

```csharp
public readonly GpuBigUFloat<TSize> Normalize(int leadingZeros = 0, bool carryOut = false);
```

#### Parameters

`leadingZeros` _int_<br>
The count of leading zeros to shift out.

`carryOut` _bool_<br>
`true` if a carry bit was produced during addition.

#### Returns

_GpuBigUFloat&lt;TSize&gt;_<br>
The normalized value.

## Round(GpuBigFloatRoundingMode, bool, bool, bool)

Applies IEEE 754 compliant rounding to the floating-point value.

```csharp
public readonly GpuBigUFloat<TSize> Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit);
```

#### Parameters

`mode` _GpuBigFloatRoundingMode_<br>
The requested IEEE 754 rounding mode.

`guardBit` _bool_<br>
The guard bit.

`roundBit` _bool_<br>
The round bit.

`stickyBit` _bool_<br>
The sticky bit.

#### Returns

_GpuBigUFloat&lt;TSize&gt;_<br>
The rounded value.

## Subtract(GpuBigUFloat&lt;TSize&gt;)

Subtracts an unsigned floating-point number from this instance.

```csharp
public readonly GpuBigUFloat<TSize> Subtract(GpuBigUFloat<TSize> other);
```

#### Parameters

`other` _GpuBigUFloat&lt;TSize&gt;_<br>
The value to subtract.

#### Returns

_GpuBigUFloat&lt;TSize&gt;_<br>
The difference.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuBigUFloat<TSize>, GpuBigUFloat<TSize>)` | Adds two unsigned floating-point numbers. |
| `Subtraction(GpuBigUFloat<TSize>, GpuBigUFloat<TSize>)` | Subtracts one unsigned floating-point number from another. |
| `Equality(GpuBigUFloat<TSize>, GpuBigUFloat<TSize>)` | Indicates whether two values are equal. |
| `Inequality(GpuBigUFloat<TSize>, GpuBigUFloat<TSize>)` | Indicates whether two values are not equal. |
| `LessThan(GpuBigUFloat<TSize>, GpuBigUFloat<TSize>)` | Returns a value indicating whether a specified value is less than another. |
| `GreaterThan(GpuBigUFloat<TSize>, GpuBigUFloat<TSize>)` | Returns a value indicating whether a specified value is greater than another. |
| `LessThanOrEqual(GpuBigUFloat<TSize>, GpuBigUFloat<TSize>)` | Returns a value indicating whether a specified value is less than or equal to another. |
| `GreaterThanOrEqual(GpuBigUFloat<TSize>, GpuBigUFloat<TSize>)` | Returns a value indicating whether a specified value is greater than or equal to another. |

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math

```csharp
// Execution Context: GPU Kernel
using System;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;

var sig = GpuUIntBigInteger<LimbBuffer4>.One;

// Represents 1.0 * 2^(-1) = 0.5
var floatVal = new GpuBigUFloat<LimbBuffer4>(significand: sig, exponent: -1);

var doubleVal = floatVal + floatVal; // 1.0
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
