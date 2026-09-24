---
title: "GpuUIntBigInteger<TSize> Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a fixed-size arbitrary-precision unsigned integer stored in value-type memory on the GPU."
---

## GpuUIntBigInteger&lt;TSize&gt; Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents a fixed-size arbitrary-precision unsigned integer stored in value-type memory on the GPU.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct GpuUIntBigInteger<TSize> : IEquatable<GpuUIntBigInteger<TSize>> where TSize : unmanaged
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuUIntBigInteger&lt;TSize&gt;(TSize)](#gpuuintbigintegertsizetsize) | Initializes a new instance of the `GpuUIntBigInteger<TSize>` struct with specified limbs. |

## GpuUIntBigInteger&lt;TSize&gt;(TSize)

Initializes a new instance of the `GpuUIntBigInteger<TSize>` struct with specified limbs.

```csharp
public GpuUIntBigInteger(TSize limbs);
```

#### Parameters

`limbs` _TSize_<br>
The limb storage payload.

## Properties

| Name | Description |
| --- | --- |
| [Limbs](#limbs) | The fixed-size limb storage backing this big integer instance. |
| [One](#one) | Gets the value `1` represented as a `GpuUIntBigInteger<TSize>`. |
| [Zero](#zero) | Gets the value `0` represented as a `GpuUIntBigInteger<TSize>`. |

## Limbs

The fixed-size limb storage backing this big integer instance.

```csharp
public TSize Limbs;
```

## One

Gets the value `1` represented as a `GpuUIntBigInteger<TSize>`.

```csharp
public static GpuUIntBigInteger<TSize> One { get; }
```

## Zero

Gets the value `0` represented as a `GpuUIntBigInteger<TSize>`.

```csharp
public static GpuUIntBigInteger<TSize> Zero { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuUIntBigInteger&lt;TSize&gt;, out bool)](#addgpuuintbigintegertsize-out-bool) | Adds two big integers and returns the carry flag. |
| [CompareTo(GpuUIntBigInteger&lt;TSize&gt;)](#comparetogpuuintbigintegertsize) | Compares this instance to another big integer. |
| [CountLeadingZeros()](#countleadingzeros) | Counts the number of leading zero bits. |
| [Equals(GpuUIntBigInteger&lt;TSize&gt;)](#equalsgpuuintbigintegertsize) | Returns a value indicating whether this instance is equal to another. |
| [Equals(object?)](#equalsobject) | Returns a value indicating whether this instance is equal to a specified object. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |
| [IsOdd()](#isodd) | Returns a value indicating whether this big integer is an odd number. |
| [IsZero()](#iszero) | Returns a value indicating whether this big integer is zero. |
| [SetMsb()](#setmsb) | Sets the most significant bit (MSB) of the fixed buffer to `1`. |
| [ShiftLeft(int)](#shiftleftint) | Shifts the bits of the value left by a specified amount. |
| [ShiftRight(int, out bool)](#shiftrightint-out-bool) | Shifts the bits of the value right by a specified amount, producing a sticky bit. |
| [ShiftRight(int, out bool, out bool, out bool)](#shiftrightint-out-bool-out-bool-out-bool) | Shifts the bits of the value right, producing guard, round, and sticky bits. |
| [Subtract(GpuUIntBigInteger&lt;TSize&gt;, out bool)](#subtractgpuuintbigintegertsize-out-bool) | Subtracts a big integer from this instance and returns the borrow flag. |

## Add(GpuUIntBigInteger&lt;TSize&gt;, out bool)

Adds two big integers and returns the carry flag.

```csharp
public readonly GpuUIntBigInteger<TSize> Add(GpuUIntBigInteger<TSize> other, out bool carry);
```

#### Parameters

`other` _GpuUIntBigInteger&lt;TSize&gt;_<br>
The value to add.

`carry` _out bool_<br>
`true` if the addition overflowed (carry out); otherwise, `false`.

#### Returns

_GpuUIntBigInteger&lt;TSize&gt;_<br>
The sum of the two values.

## CompareTo(GpuUIntBigInteger&lt;TSize&gt;)

Compares this instance to another big integer.

```csharp
public readonly int CompareTo(GpuUIntBigInteger<TSize> other);
```

#### Parameters

`other` _GpuUIntBigInteger&lt;TSize&gt;_<br>
The value to compare to.

#### Returns

_int_<br>
A signed integer that indicates the relative order of the values being compared.

## CountLeadingZeros()

Counts the number of leading zero bits.

```csharp
public readonly int CountLeadingZeros();
```

#### Returns

_int_<br>
The number of leading zeros.

## Equals(GpuUIntBigInteger&lt;TSize&gt;)

Returns a value indicating whether this instance is equal to another.

```csharp
public readonly bool Equals(GpuUIntBigInteger<TSize> other);
```

#### Parameters

`other` _GpuUIntBigInteger&lt;TSize&gt;_<br>
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

## IsOdd()

Returns a value indicating whether this big integer is an odd number.

```csharp
public readonly bool IsOdd();
```

#### Returns

_bool_<br>
`true` if the value is odd; otherwise, `false`.

## IsZero()

Returns a value indicating whether this big integer is zero.

```csharp
public readonly bool IsZero();
```

#### Returns

_bool_<br>
`true` if the value is zero; otherwise, `false`.

## SetMsb()

Sets the most significant bit (MSB) of the fixed buffer to `1`.

```csharp
public GpuUIntBigInteger<TSize> SetMsb();
```

#### Returns

_GpuUIntBigInteger&lt;TSize&gt;_<br>
A new instance with the MSB set.

## ShiftLeft(int)

Shifts the bits of the value left by a specified amount.

```csharp
public readonly GpuUIntBigInteger<TSize> ShiftLeft(int bits);
```

#### Parameters

`bits` _int_<br>
The number of bits to shift.

#### Returns

_GpuUIntBigInteger&lt;TSize&gt;_<br>
The shifted value.

## ShiftRight(int, out bool)

Shifts the bits of the value right by a specified amount, producing a sticky bit.

```csharp
public readonly GpuUIntBigInteger<TSize> ShiftRight(int bits, out bool stickyBit);
```

#### Parameters

`bits` _int_<br>
The number of bits to shift.

`stickyBit` _out bool_<br>
Set to `true` if any non-zero bits were shifted out past the LSB.

#### Returns

_GpuUIntBigInteger&lt;TSize&gt;_<br>
The shifted value.

## ShiftRight(int, out bool, out bool, out bool)

Shifts the bits of the value right, producing guard, round, and sticky bits.

```csharp
public readonly GpuUIntBigInteger<TSize> ShiftRight(int bits, out bool guardBit, out bool roundBit, out bool stickyBit);
```

#### Parameters

`bits` _int_<br>
The number of bits to shift.

`guardBit` _out bool_<br>
The first bit shifted out (the half bit).

`roundBit` _out bool_<br>
The second bit shifted out (the quarter bit).

`stickyBit` _out bool_<br>
Set to `true` if any bits after the round bit were non-zero.

#### Returns

_GpuUIntBigInteger&lt;TSize&gt;_<br>
The shifted value.

## Subtract(GpuUIntBigInteger&lt;TSize&gt;, out bool)

Subtracts a big integer from this instance and returns the borrow flag.

```csharp
public readonly GpuUIntBigInteger<TSize> Subtract(GpuUIntBigInteger<TSize> other, out bool borrow);
```

#### Parameters

`other` _GpuUIntBigInteger&lt;TSize&gt;_<br>
The value to subtract.

`borrow` _out bool_<br>
`true` if the subtraction underflowed (borrow out); otherwise, `false`.

#### Returns

_GpuUIntBigInteger&lt;TSize&gt;_<br>
The difference of the two values.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuUIntBigInteger<TSize>, GpuUIntBigInteger<TSize>)` | Adds two big integers. |
| `Subtraction(GpuUIntBigInteger<TSize>, GpuUIntBigInteger<TSize>)` | Subtracts one big integer from another. |
| `Equality(GpuUIntBigInteger<TSize>, GpuUIntBigInteger<TSize>)` | Indicates whether two `GpuUIntBigInteger<TSize>` values are equal. |
| `Inequality(GpuUIntBigInteger<TSize>, GpuUIntBigInteger<TSize>)` | Indicates whether two `GpuUIntBigInteger<TSize>` values are not equal. |
| `LessThan(GpuUIntBigInteger<TSize>, GpuUIntBigInteger<TSize>)` | Returns a value indicating whether a specified `GpuUIntBigInteger<TSize>` is less than another. |
| `GreaterThan(GpuUIntBigInteger<TSize>, GpuUIntBigInteger<TSize>)` | Returns a value indicating whether a specified `GpuUIntBigInteger<TSize>` is greater than another. |
| `LessThanOrEqual(GpuUIntBigInteger<TSize>, GpuUIntBigInteger<TSize>)` | Returns a value indicating whether a specified `GpuUIntBigInteger<TSize>` is less than or equal to another. |
| `GreaterThanOrEqual(GpuUIntBigInteger<TSize>, GpuUIntBigInteger<TSize>)` | Returns a value indicating whether a specified `GpuUIntBigInteger<TSize>` is greater than or equal to another. |

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math

```csharp
// Execution Context: GPU Kernel
using System;
using Kassyi.GSharp.Stdlib.Math.BigInt;

var a = GpuUIntBigInteger<LimbBuffer4>.One;
var b = new GpuUIntBigInteger<LimbBuffer4>(new LimbBuffer4 { V0 = 42 });

var sum = a + b;
var isZero = sum.IsZero();
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
