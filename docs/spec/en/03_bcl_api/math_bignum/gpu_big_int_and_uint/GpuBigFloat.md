---
title: "GpuBigFloat<TSize> Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents an arbitrary-precision signed floating-point number on the GPU using an unsigned floating-point value and a sign flag."
---

## GpuBigFloat&lt;TSize&gt; Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigFloat`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents an arbitrary-precision signed floating-point number on the GPU using an unsigned floating-point value and a sign flag.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigFloat<TSize> : IEquatable<GpuBigFloat<TSize>>, IComparable<GpuBigFloat<TSize>> where TSize : unmanaged
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigFloat&lt;TSize&gt;(GpuBigUFloat&lt;TSize&gt;, bool)](#gpubigfloatt-sizegpubigufloatt-size-bool) | Initializes a new instance of the `GpuBigFloat<TSize>` struct. |
| [GpuBigFloat&lt;TSize&gt;(GpuUIntBigInteger&lt;TSize&gt;, int, bool)](#gpubigfloatt-sizegpuuintbigintegert-size-int-bool) | Initializes a new instance of the `GpuBigFloat<TSize>` struct from significand, exponent, and sign flag. |

## GpuBigFloat&lt;TSize&gt;(GpuBigUFloat&lt;TSize&gt;, bool)

Initializes a new instance of the `GpuBigFloat<TSize>` struct.

```csharp
public GpuBigFloat(GpuBigUFloat<TSize> uFloat, bool isNegative);
```

#### Parameters

`uFloat` _GpuBigUFloat&lt;TSize&gt;_<br>
The unsigned float component.

`isNegative` _bool_<br>
`true` if negative; otherwise, `false`.

## GpuBigFloat&lt;TSize&gt;(GpuUIntBigInteger&lt;TSize&gt;, int, bool)

Initializes a new instance of the `GpuBigFloat<TSize>` struct from significand, exponent, and sign flag.

```csharp
public GpuBigFloat(GpuUIntBigInteger<TSize> significand, int exponent, bool isNegative);
```

#### Parameters

`significand` _GpuUIntBigInteger&lt;TSize&gt;_<br>
The significand payload.

`exponent` _int_<br>
The binary exponent.

`isNegative` _bool_<br>
`true` if negative; otherwise, `false`.

## Properties

| Name | Description |
| --- | --- |
| [IsNegative](#isnegative) | Gets or sets a value indicating whether the floating-point number is negative. |
| [UFloat](#ufloat) | The underlying unsigned floating-point value payload. |

## IsNegative

Gets or sets a value indicating whether the floating-point number is negative.

```csharp
public bool IsNegative;
```

## UFloat

The underlying unsigned floating-point value payload.

```csharp
public GpuBigUFloat<TSize> UFloat;
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigFloat&lt;TSize&gt;)](#addgpubigfloatt-size) | Adds two `GpuBigFloat<TSize>` values. |
| [AlignRadixPoint(GpuBigFloat&lt;TSize&gt;, out GpuBigFloat&lt;TSize&gt;, out GpuBigFloat&lt;TSize&gt;, out bool, out bool)](#alignradixpointgpubigfloatt-size-out-gpubigfloatt-size-out-gpubigfloatt-size-out-bool-out-bool) | Aligns the radix point between this instance and another `GpuBigFloat<TSize>`. |
| [CompareTo(GpuBigFloat&lt;TSize&gt;)](#comparetogpubigfloatt-size) | Compares this instance to another signed floating-point number. |
| [Equals(GpuBigFloat&lt;TSize&gt;)](#equalsgpubigfloatt-size) | Returns a value indicating whether this instance is equal to another. |
| [Equals(object?)](#equalsobject) | Returns a value indicating whether this instance is equal to a specified object. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |
| [Normalize(int, bool)](#normalizeint-bool) | Normalizes the floating-point significand and adjusts the exponent. |
| [Round(GpuBigFloatRoundingMode, bool, bool, bool)](#roundgpubigfloatroundingmode-bool-bool-bool) | Applies IEEE 754 compliant rounding to the floating-point value. |
| [Subtract(GpuBigFloat&lt;TSize&gt;)](#subtractgpubigfloatt-size) | Subtracts one `GpuBigFloat<TSize>` value from another. |

## Add(GpuBigFloat&lt;TSize&gt;)

Adds two `GpuBigFloat<TSize>` values.

```csharp
public readonly GpuBigFloat<TSize> Add(GpuBigFloat<TSize> other);
```

#### Parameters

`other` _GpuBigFloat&lt;TSize&gt;_<br>
The value to add.

#### Returns

_GpuBigFloat&lt;TSize&gt;_<br>
The sum of the two values.

## AlignRadixPoint(GpuBigFloat&lt;TSize&gt;, out GpuBigFloat&lt;TSize&gt;, out GpuBigFloat&lt;TSize&gt;, out bool, out bool)

Aligns the radix point between this instance and another `GpuBigFloat<TSize>`.

```csharp
public readonly void AlignRadixPoint(GpuBigFloat<TSize> other, out GpuBigFloat<TSize> alignedThis, out GpuBigFloat<TSize> alignedOther, out bool stickyThis, out bool stickyOther);
```

#### Parameters

`other` _GpuBigFloat&lt;TSize&gt;_<br>
The operand to align against.

`alignedThis` _out GpuBigFloat&lt;TSize&gt;_<br>
The resulting aligned this operand.

`alignedOther` _out GpuBigFloat&lt;TSize&gt;_<br>
The resulting aligned other operand.

`stickyThis` _out bool_<br>
The sticky bit preserved from right-shifting this operand.

`stickyOther` _out bool_<br>
The sticky bit preserved from right-shifting the other operand.

## CompareTo(GpuBigFloat&lt;TSize&gt;)

Compares this instance to another signed floating-point number.

```csharp
public readonly int CompareTo(GpuBigFloat<TSize> other);
```

#### Parameters

`other` _GpuBigFloat&lt;TSize&gt;_<br>
The value to compare to.

#### Returns

_int_<br>
A signed integer that indicates the relative order of the values being compared.

## Equals(GpuBigFloat&lt;TSize&gt;)

Returns a value indicating whether this instance is equal to another.

```csharp
public readonly bool Equals(GpuBigFloat<TSize> other);
```

#### Parameters

`other` _GpuBigFloat&lt;TSize&gt;_<br>
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
public readonly GpuBigFloat<TSize> Normalize(int leadingZeros = 0, bool carryOut = false);
```

#### Parameters

`leadingZeros` _int_<br>
The count of leading zeros to shift out.

`carryOut` _bool_<br>
`true` if a carry bit was produced during addition.

#### Returns

_GpuBigFloat&lt;TSize&gt;_<br>
The normalized value.

## Round(GpuBigFloatRoundingMode, bool, bool, bool)

Applies IEEE 754 compliant rounding to the floating-point value.

```csharp
public readonly GpuBigFloat<TSize> Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit);
```

#### Parameters

`mode` _GpuBigFloatRoundingMode_<br>
The requested IEEE 754 rounding mode.

`guardBit` _bool_<br>
The guard bit ($G$).

`roundBit` _bool_<br>
The round bit ($R$).

`stickyBit` _bool_<br>
The sticky bit ($S$).

#### Returns

_GpuBigFloat&lt;TSize&gt;_<br>
The rounded value.

## Subtract(GpuBigFloat&lt;TSize&gt;)

Subtracts one `GpuBigFloat<TSize>` value from another.

```csharp
public readonly GpuBigFloat<TSize> Subtract(GpuBigFloat<TSize> other);
```

#### Parameters

`other` _GpuBigFloat&lt;TSize&gt;_<br>
The value to subtract.

#### Returns

_GpuBigFloat&lt;TSize&gt;_<br>
The difference.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuBigFloat<TSize>, GpuBigFloat<TSize>)` | Adds two signed floating-point numbers. |
| `Subtraction(GpuBigFloat<TSize>, GpuBigFloat<TSize>)` | Subtracts one signed floating-point number from another. |
| `Equality(GpuBigFloat<TSize>, GpuBigFloat<TSize>)` | Indicates whether two values are equal. |
| `Inequality(GpuBigFloat<TSize>, GpuBigFloat<TSize>)` | Indicates whether two values are not equal. |
| `LessThan(GpuBigFloat<TSize>, GpuBigFloat<TSize>)` | Returns a value indicating whether a specified value is less than another. |
| `GreaterThan(GpuBigFloat<TSize>, GpuBigFloat<TSize>)` | Returns a value indicating whether a specified value is greater than another. |
| `LessThanOrEqual(GpuBigFloat<TSize>, GpuBigFloat<TSize>)` | Returns a value indicating whether a specified value is less than or equal to another. |
| `GreaterThanOrEqual(GpuBigFloat<TSize>, GpuBigFloat<TSize>)` | Returns a value indicating whether a specified value is greater than or equal to another. |

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math

```csharp
// Execution Context: GPU Kernel
using System;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;

var sig = GpuUIntBigInteger<LimbBuffer4>.One;

// Represents -(1.0 * 2^(-1)) = -0.5
var floatVal = new GpuBigFloat<LimbBuffer4>(significand: sig, exponent: -1, isNegative: true);

var zeroVal = floatVal + new GpuBigFloat<LimbBuffer4>(sig, -1, false);
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuContext](../../../03_bcl_api/math_bignum/gpu_context/GpuContext.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
