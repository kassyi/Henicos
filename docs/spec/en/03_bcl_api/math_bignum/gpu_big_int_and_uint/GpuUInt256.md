---
title: "GpuUInt256 Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a fixed-width 256-bit unsigned integer stored in value-type memory on the GPU."
---

## GpuUInt256 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a fixed-width 256-bit unsigned integer stored in value-type memory on the GPU.

```csharp
[CollectionBuilder(typeof(GpuUInt256), nameof(Create))]
[GenerateGpuNumberOperators]
public partial struct GpuUInt256 : IEquatable<GpuUInt256>, IComparable<GpuUInt256>
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuUInt256(GpuBigUInt&lt;LimbBuffer8&gt;)](#gpuuint256gpubiguintlimbbuffer8) | Initializes a new instance of the `GpuUInt256` struct from an existing buffer. |
| [GpuUInt256(uint)](#gpuuint256uint) | Initializes a new instance of the `GpuUInt256` struct with a 32-bit unsigned integer value. |

## GpuUInt256(GpuBigUInt&lt;LimbBuffer8&gt;)

Initializes a new instance of the `GpuUInt256` struct from an existing buffer.

```csharp
public GpuUInt256(GpuBigUInt<LimbBuffer8> value);
```

#### Parameters

`value` _GpuBigUInt&lt;LimbBuffer8&gt;_<br>
The underlying 256-bit fixed-size unsigned big integer storage.

## GpuUInt256(uint)

Initializes a new instance of the `GpuUInt256` struct initialized with a 32-bit unsigned integer value.

```csharp
public GpuUInt256(uint value);
```

#### Parameters

`value` _uint_<br>
The 32-bit unsigned integer value.

## Fields

| Name | Description |
| --- | --- |
| [Value](#value) | The underlying 256-bit fixed-size unsigned big integer storage. |

## Value

The underlying 256-bit fixed-size unsigned big integer storage.

```csharp
public GpuBigUInt<LimbBuffer8> Value;
```

## Methods

| Name | Description |
| --- | --- |
| [CompareTo(GpuUInt256)](#comparetogpuuint256) | Compares this instance to another `GpuUInt256` and returns an integer that indicates their relative order. |
| [Create(ReadOnlySpan&lt;uint&gt;)](#createreadonlyspanuint) | Creates a new `GpuUInt256` instance from the specified sequence of 32-bit limb words. |
| [Equals(GpuUInt256)](#equalsgpuuint256) | Indicates whether the current instance is equal to another `GpuUInt256` instance. |
| [Equals(object?)](#equalsobject) | Determines whether the specified object is equal to the current instance. |
| [GetEnumerator()](#getenumerator) | Returns a dummy enumerator to enable C# collection expression support. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |

## CompareTo(GpuUInt256)

Compares this instance to another `GpuUInt256` and returns an integer that indicates whether the value of this instance is less than, equal to, or greater than the value of the specified instance.

```csharp
public readonly int CompareTo(GpuUInt256 other);
```

#### Parameters

`other` _GpuUInt256_<br>
A `GpuUInt256` to compare.

#### Returns

_int_<br>
A signed integer that indicates the relative values of this instance and `other`.

## Create(ReadOnlySpan&lt;uint&gt;)

Creates a new `GpuUInt256` instance from the specified sequence of 32-bit limb words.

```csharp
public static GpuUInt256 Create(ReadOnlySpan<uint> elements);
```

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
A read-only span containing the 32-bit unsigned limbs in little-endian order.

#### Returns

_GpuUInt256_<br>
A new `GpuUInt256` instance containing the specified elements.

## Equals(GpuUInt256)

Indicates whether the current instance is equal to another `GpuUInt256` instance.

```csharp
public readonly bool Equals(GpuUInt256 other);
```

#### Parameters

`other` _GpuUInt256_<br>
A `GpuUInt256` to compare with this instance.

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
`true` if `obj` is a `GpuUInt256` and equals the current instance; otherwise, `false`.

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
| `Addition(GpuUInt256, GpuUInt256)` | Adds two `GpuUInt256` values. |
| `Subtraction(GpuUInt256, GpuUInt256)` | Subtracts one `GpuUInt256` value from another. |
| `Equality(GpuUInt256, GpuUInt256)` | Determines whether two `GpuUInt256` values are equal. |
| `Inequality(GpuUInt256, GpuUInt256)` | Determines whether two `GpuUInt256` values are not equal. |
| `LessThan(GpuUInt256, GpuUInt256)` | Determines whether one `GpuUInt256` value is less than another. |
| `GreaterThan(GpuUInt256, GpuUInt256)` | Determines whether one `GpuUInt256` value is greater than another. |
| `LessThanOrEqual(GpuUInt256, GpuUInt256)` | Determines whether one `GpuUInt256` value is less than or equal to another. |
| `GreaterThanOrEqual(GpuUInt256, GpuUInt256)` | Determines whether one `GpuUInt256` value is greater than or equal to another. |
| `Implicit(GpuUInt256 to GpuBigUInt<LimbBuffer8>)` | Implicitly converts a `GpuUInt256` to its underlying `GpuBigUInt<LimbBuffer8>` representation. |
| `Implicit(GpuBigUInt<LimbBuffer8> to GpuUInt256)` | Implicitly converts a `GpuBigUInt<LimbBuffer8>` value to a `GpuUInt256` instance. |
| `Implicit(uint to GpuUInt256)` | Implicitly converts a 32-bit unsigned integer to a `GpuUInt256` instance. |

## Examples

### Creating GpuUInt256 Instances

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU) or G# Kernel (GPU)

// 1. Create a 256-bit unsigned integer from a 32-bit scalar value
GpuUInt256 fromScalar = new GpuUInt256(42u);

// 2. Implicit conversion from uint is supported
GpuUInt256 implicitConversion = 100u;

// 3. Create from an array of eight 32-bit limbs (CollectionBuilder syntax)
GpuUInt256 fromLimbs = [0x00000001, 0, 0, 0, 0, 0, 0, 0];

// 4. Retrieve the underlying GpuBigUInt<LimbBuffer8> implicitly
GpuBigUInt<LimbBuffer8> underlyingBuffer = fromLimbs;

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
