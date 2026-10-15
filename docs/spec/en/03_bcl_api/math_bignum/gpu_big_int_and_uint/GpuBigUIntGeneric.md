---
title: "GpuBigUInt<TSize> Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a fixed-size arbitrary-precision unsigned integer stored in value-type memory on the GPU."
---

## GpuBigUInt&lt;TSize&gt; Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a fixed-size arbitrary-precision unsigned integer stored in value-type memory on the GPU.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigUInt<TSize> : IEquatable<GpuBigUInt<TSize>>, IComparable<GpuBigUInt<TSize>> where TSize : unmanaged
```

### Type Parameters

`TSize` _unmanaged_<br>
The unmanaged struct type representing the limb storage layout (e.g., `LimbBuffer4`, `LimbBuffer8`, `LimbBuffer16`).

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigUInt&lt;TSize&gt;(TSize)](#gpubiguinttsizetsize) | Initializes a new instance of the `GpuBigUInt<TSize>` struct with specified limbs. |

## GpuBigUInt&lt;TSize&gt;(TSize)

Initializes a new instance of the `GpuBigUInt<TSize>` struct with specified limbs.

```csharp
public GpuBigUInt(TSize limbs);
```

#### Parameters

`limbs` _TSize_<br>
The limb storage payload.

## Fields / Properties

| Name | Description |
| --- | --- |
| [Limbs](#limbs) | The fixed-size limb storage backing this big integer instance. |
| [One](#one) | Gets the value `1` represented as a `GpuBigUInt<TSize>`. |
| [Zero](#zero) | Gets the value `0` represented as a `GpuBigUInt<TSize>`. |

## Limbs

The fixed-size limb storage backing this big integer instance.

```csharp
public TSize Limbs;
```

## One

Gets the value `1` represented as a `GpuBigUInt<TSize>`.

```csharp
public static GpuBigUInt<TSize> One { get; }
```

## Zero

Gets the value `0` represented as a `GpuBigUInt<TSize>`.

```csharp
public static GpuBigUInt<TSize> Zero { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigUInt&lt;TSize&gt;, out bool)](#addgpubiguinttsize-out-bool) | Adds two big integers and returns the carry flag. |
| [CompareTo(GpuBigUInt&lt;TSize&gt;)](#comparetogpubiguinttsize) | Compares this instance to another big integer. |
| [CountLeadingZeros()](#countleadingzeros) | Counts the number of leading zero bits. |
| [Equals(GpuBigUInt&lt;TSize&gt;)](#equalsgpubiguinttsize) | Returns a value indicating whether this instance is equal to another. |
| [Equals(object?)](#equalsobject) | Returns a value indicating whether this instance is equal to a specified object. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |
| [IsOdd()](#isodd) | Returns a value indicating whether this big integer is an odd number. |
| [IsZero()](#iszero) | Returns a value indicating whether this big integer is zero. |
| [SetMsb()](#setmsb) | Sets the most significant bit (MSB) of the fixed buffer to `1`. |
| [ShiftLeft(int)](#shiftleftint) | Shifts the bits of the value left by a specified amount. |
| [ShiftRight(int, out bool)](#shiftrightint-out-bool) | Shifts the bits of the value right by a specified amount, producing a sticky bit. |
| [ShiftRight(int, out bool, out bool, out bool)](#shiftrightint-out-bool-out-bool-out-bool) | Shifts the bits of the value right, producing guard, round, and sticky bits. |
| [Subtract(GpuBigUInt&lt;TSize&gt;, out bool)](#subtractgpubiguinttsize-out-bool) | Subtracts a big integer from this instance and returns the borrow flag. |

## Add(GpuBigUInt&lt;TSize&gt;, out bool)

Adds two big integers and returns the carry flag.

```csharp
public readonly GpuBigUInt<TSize> Add(GpuBigUInt<TSize> other, out bool carry);
```

#### Parameters

`other` _GpuBigUInt&lt;TSize&gt;_<br>
The value to add.

`carry` _out bool_<br>
`true` if the addition overflowed (carry out); otherwise, `false`.

#### Returns

_GpuBigUInt&lt;TSize&gt;_<br>
The sum of the two values.

## CompareTo(GpuBigUInt&lt;TSize&gt;)

Compares this instance to another big integer.

```csharp
public readonly int CompareTo(GpuBigUInt<TSize> other);
```

#### Parameters

`other` _GpuBigUInt&lt;TSize&gt;_<br>
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

## Equals(GpuBigUInt&lt;TSize&gt;)

Returns a value indicating whether this instance is equal to another.

```csharp
public readonly bool Equals(GpuBigUInt<TSize> other);
```

#### Parameters

`other` _GpuBigUInt&lt;TSize&gt;_<br>
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
public GpuBigUInt<TSize> SetMsb();
```

#### Returns

_GpuBigUInt&lt;TSize&gt;_<br>
A new instance with the MSB set.

## ShiftLeft(int)

Shifts the bits of the value left by a specified amount.

```csharp
public readonly GpuBigUInt<TSize> ShiftLeft(int bits);
```

#### Parameters

`bits` _int_<br>
The number of bits to shift.

#### Returns

_GpuBigUInt&lt;TSize&gt;_<br>
The shifted value.

## ShiftRight(int, out bool)

Shifts the bits of the value right by a specified amount, producing a sticky bit.

```csharp
public readonly GpuBigUInt<TSize> ShiftRight(int bits, out bool stickyBit);
```

#### Parameters

`bits` _int_<br>
The number of bits to shift.

`stickyBit` _out bool_<br>
Set to `true` if any non-zero bits were shifted out past the LSB.

#### Returns

_GpuBigUInt&lt;TSize&gt;_<br>
The shifted value.

## ShiftRight(int, out bool, out bool, out bool)

Shifts the bits of the value right, producing guard, round, and sticky bits.

```csharp
public readonly GpuBigUInt<TSize> ShiftRight(int bits, out bool guardBit, out bool roundBit, out bool stickyBit);
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

_GpuBigUInt&lt;TSize&gt;_<br>
The shifted value.

## Subtract(GpuBigUInt&lt;TSize&gt;, out bool)

Subtracts a big integer from this instance and returns the borrow flag.

```csharp
public readonly GpuBigUInt<TSize> Subtract(GpuBigUInt<TSize> other, out bool borrow);
```

#### Parameters

`other` _GpuBigUInt&lt;TSize&gt;_<br>
The value to subtract.

`borrow` _out bool_<br>
`true` if the subtraction underflowed (borrow out); otherwise, `false`.

#### Returns

_GpuBigUInt&lt;TSize&gt;_<br>
The difference of the two values.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuBigUInt<TSize>, GpuBigUInt<TSize>)` | Adds two big integers. |
| `Subtraction(GpuBigUInt<TSize>, GpuBigUInt<TSize>)` | Subtracts one big integer from another. |
| `Equality(GpuBigUInt<TSize>, GpuBigUInt<TSize>)` | Indicates whether two `GpuBigUInt<TSize>` values are equal. |
| `Inequality(GpuBigUInt<TSize>, GpuBigUInt<TSize>)` | Indicates whether two `GpuBigUInt<TSize>` values are not equal. |
| `LessThan(GpuBigUInt<TSize>, GpuBigUInt<TSize>)` | Returns a value indicating whether a specified `GpuBigUInt<TSize>` is less than another. |
| `GreaterThan(GpuBigUInt<TSize>, GpuBigUInt<TSize>)` | Returns a value indicating whether a specified `GpuBigUInt<TSize>` is greater than another. |
| `LessThanOrEqual(GpuBigUInt<TSize>, GpuBigUInt<TSize>)` | Returns a value indicating whether a specified `GpuBigUInt<TSize>` is less than or equal to another. |
| `GreaterThanOrEqual(GpuBigUInt<TSize>, GpuBigUInt<TSize>)` | Returns a value indicating whether a specified `GpuBigUInt<TSize>` is greater than or equal to another. |

## Examples

### Fixed-Size Arithmetic Operations

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU) or G# Kernel
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

var a = GpuBigUInt<LimbBuffer4>.One;

var buffer = new LimbBuffer4();
buffer[0] = 42;
var b = new GpuBigUInt<LimbBuffer4>(buffer);

var sum = a + b;
var isZero = sum.IsZero();

Console.WriteLine($"sum.Limbs[0] = {sum.Limbs[0]}");
Console.WriteLine($"isZero = {isZero}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
