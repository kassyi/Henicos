---
title: "GpuUInt128 Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a fixed-width 128-bit unsigned integer stored in value-type memory on the GPU."
---

## GpuUInt128 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a fixed-width 128-bit unsigned integer stored in value-type memory on the GPU.

```csharp
[CollectionBuilder(typeof(GpuUInt128), nameof(Create))]
[GenerateGpuNumberOperators]
public partial struct GpuUInt128 : IEquatable<GpuUInt128>, IComparable<GpuUInt128>
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuUInt128(GpuBigUInt&lt;LimbBuffer4&gt;)](#gpuuint128gpubiguintlimbbuffer4) | Initializes a new instance of the `GpuUInt128` struct from an existing buffer. |
| [GpuUInt128(uint)](#gpuuint128uint) | Initializes a new instance of the `GpuUInt128` struct with a 32-bit unsigned integer value. |

## GpuUInt128(GpuBigUInt&lt;LimbBuffer4&gt;)

Initializes a new instance of the `GpuUInt128` struct from an existing buffer.

```csharp
public GpuUInt128(GpuBigUInt<LimbBuffer4> value);
```

#### Parameters

`value` _GpuBigUInt&lt;LimbBuffer4&gt;_<br>
The underlying 128-bit fixed-size unsigned big integer storage.

## GpuUInt128(uint)

Initializes a new instance of the `GpuUInt128` struct initialized with a 32-bit unsigned integer value.

```csharp
public GpuUInt128(uint value);
```

#### Parameters

`value` _uint_<br>
The 32-bit unsigned integer value.

## Fields

| Name | Description |
| --- | --- |
| [Value](#value) | The underlying 128-bit fixed-size unsigned big integer storage. |

## Value

The underlying 128-bit fixed-size unsigned big integer storage.

```csharp
public GpuBigUInt<LimbBuffer4> Value;
```

## Methods

| Name | Description |
| --- | --- |
| [CompareTo(GpuUInt128)](#comparetogpuuint128) | Compares this instance to another `GpuUInt128` and returns an integer that indicates their relative order. |
| [Create(ReadOnlySpan&lt;uint&gt;)](#createreadonlyspanuint) | Creates a new `GpuUInt128` instance from the specified sequence of 32-bit limb words. |
| [Equals(GpuUInt128)](#equalsgpuuint128) | Indicates whether the current instance is equal to another `GpuUInt128` instance. |
| [Equals(object?)](#equalsobject) | Determines whether the specified object is equal to the current instance. |
| [GetEnumerator()](#getenumerator) | Returns a dummy enumerator to enable C# collection expression support. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |

## CompareTo(GpuUInt128)

Compares this instance to another `GpuUInt128` and returns an integer that indicates whether the value of this instance is less than, equal to, or greater than the value of the specified instance.

```csharp
public readonly int CompareTo(GpuUInt128 other);
```

#### Parameters

`other` _GpuUInt128_<br>
A `GpuUInt128` to compare.

#### Returns

_int_<br>
A signed integer that indicates the relative values of this instance and `other`.

## Create(ReadOnlySpan&lt;uint&gt;)

Creates a new `GpuUInt128` instance from the specified sequence of 32-bit limb words.

```csharp
public static GpuUInt128 Create(ReadOnlySpan<uint> elements);
```

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
A read-only span containing the 32-bit unsigned limbs in little-endian order.

#### Returns

_GpuUInt128_<br>
A new `GpuUInt128` instance containing the specified elements.

## Equals(GpuUInt128)

Indicates whether the current instance is equal to another `GpuUInt128` instance.

```csharp
public readonly bool Equals(GpuUInt128 other);
```

#### Parameters

`other` _GpuUInt128_<br>
A `GpuUInt128` to compare with this instance.

#### Returns

_bool_<br>
`true` if the current instance is equal to the `other` parameter; otherwise, `false`.

## Equals(object?)

Determines whether the specified object is equal to the current instance.

```csharp
public readonly override bool Equals(object? obj);
```

#### Parameters

`obj` _object?_<br>
The object to compare with the current instance.

#### Returns

_bool_<br>
`true` if `obj` is a `GpuUInt128` and equals the current instance; otherwise, `false`.

## GetEnumerator()

Returns a dummy enumerator to enable C# collection expression support.

```csharp
public DummyUIntEnumerator GetEnumerator();
```

#### Returns

_DummyUIntEnumerator_<br>
A default instance of `DummyUIntEnumerator`.

## GetHashCode()

Returns the hash code for this instance.

```csharp
public readonly override int GetHashCode();
```

#### Returns

_int_<br>
A 32-bit signed integer hash code.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuUInt128, GpuUInt128)` | Adds two `GpuUInt128` values. |
| `Subtraction(GpuUInt128, GpuUInt128)` | Subtracts one `GpuUInt128` value from another. |
| `Equality(GpuUInt128, GpuUInt128)` | Determines whether two `GpuUInt128` values are equal. |
| `Inequality(GpuUInt128, GpuUInt128)` | Determines whether two `GpuUInt128` values are not equal. |
| `LessThan(GpuUInt128, GpuUInt128)` | Determines whether one `GpuUInt128` value is less than another. |
| `GreaterThan(GpuUInt128, GpuUInt128)` | Determines whether one `GpuUInt128` value is greater than another. |
| `LessThanOrEqual(GpuUInt128, GpuUInt128)` | Determines whether one `GpuUInt128` value is less than or equal to another. |
| `GreaterThanOrEqual(GpuUInt128, GpuUInt128)` | Determines whether one `GpuUInt128` value is greater than or equal to another. |
| `Implicit(GpuUInt128 to GpuBigUInt<LimbBuffer4>)` | Implicitly converts a `GpuUInt128` to its underlying `GpuBigUInt<LimbBuffer4>` representation. |
| `Implicit(GpuBigUInt<LimbBuffer4> to GpuUInt128)` | Implicitly converts a `GpuBigUInt<LimbBuffer4>` value to a `GpuUInt128` instance. |
| `Implicit(uint to GpuUInt128)` | Implicitly converts a 32-bit unsigned integer to a `GpuUInt128` instance. |

## Examples

### Creating GpuUInt128 Instances

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU) or G# Kernel (GPU)

// 1. Create a 128-bit unsigned integer from a 32-bit scalar value
GpuUInt128 fromScalar = new GpuUInt128(42u);

// 2. Implicit conversion from uint is supported
GpuUInt128 implicitConversion = 100u;

// 3. Create from an array of four 32-bit limbs (CollectionBuilder syntax)
GpuUInt128 fromLimbs = [0x00000001, 0x00000000, 0x00000000, 0x00000000];

// 4. Retrieve the underlying GpuBigUInt<LimbBuffer4> implicitly
GpuBigUInt<LimbBuffer4> underlyingBuffer = fromLimbs;

Console.WriteLine($"fromScalar.Value.Limbs[0] = {fromScalar.Value.Limbs[0]}");
Console.WriteLine($"implicitConversion.Value.Limbs[0] = {implicitConversion.Value.Limbs[0]}");
Console.WriteLine($"underlyingBuffer.Limbs[0] = {underlyingBuffer.Limbs[0]}");
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
