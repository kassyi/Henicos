---
title: "GpuUInt512 Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a fixed-width 512-bit unsigned integer stored in value-type memory on the GPU."
---

## GpuUInt512 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a fixed-width 512-bit unsigned integer stored in value-type memory on the GPU.

```csharp
[CollectionBuilder(typeof(GpuUInt512), nameof(Create))]
[GenerateGpuNumberOperators]
public partial struct GpuUInt512 : IEquatable<GpuUInt512>, IComparable<GpuUInt512>
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuUInt512(GpuBigUInt&lt;LimbBuffer16&gt;)](#gpuuint512gpubiguintlimbbuffer16) | Initializes a new instance of the `GpuUInt512` struct from an existing buffer. |
| [GpuUInt512(uint)](#gpuuint512uint) | Initializes a new instance of the `GpuUInt512` struct with a 32-bit unsigned integer value. |

## GpuUInt512(GpuBigUInt&lt;LimbBuffer16&gt;)

Initializes a new instance of the `GpuUInt512` struct from an existing buffer.

```csharp
public GpuUInt512(GpuBigUInt<LimbBuffer16> value);
```

#### Parameters

`value` _GpuBigUInt&lt;LimbBuffer16&gt;_<br>
The underlying 512-bit fixed-size unsigned big integer storage.

## GpuUInt512(uint)

Initializes a new instance of the `GpuUInt512` struct initialized with a 32-bit unsigned integer value.

```csharp
public GpuUInt512(uint value);
```

#### Parameters

`value` _uint_<br>
The 32-bit unsigned integer value.

## Fields

| Name | Description |
| --- | --- |
| [Value](#value) | The underlying 512-bit fixed-size unsigned big integer storage. |

## Value

The underlying 512-bit fixed-size unsigned big integer storage.

```csharp
public GpuBigUInt<LimbBuffer16> Value;
```

## Methods

| Name | Description |
| --- | --- |
| [CompareTo(GpuUInt512)](#comparetogpuuint512) | Compares this instance to another `GpuUInt512` and returns an integer that indicates their relative order. |
| [Create(ReadOnlySpan&lt;uint&gt;)](#createreadonlyspanuint) | Creates a new `GpuUInt512` instance from the specified sequence of 32-bit limb words. |
| [Equals(GpuUInt512)](#equalsgpuuint512) | Indicates whether the current instance is equal to another `GpuUInt512` instance. |
| [Equals(object?)](#equalsobject) | Determines whether the specified object is equal to the current instance. |
| [GetEnumerator()](#getenumerator) | Returns a dummy enumerator to enable C# collection expression support. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |

## CompareTo(GpuUInt512)

Compares this instance to another `GpuUInt512` and returns an integer that indicates whether the value of this instance is less than, equal to, or greater than the value of the specified instance.

```csharp
public readonly int CompareTo(GpuUInt512 other);
```

#### Parameters

`other` _GpuUInt512_<br>
A `GpuUInt512` to compare.

#### Returns

_int_<br>
A signed integer that indicates the relative values of this instance and `other`.

## Create(ReadOnlySpan&lt;uint&gt;)

Creates a new `GpuUInt512` instance from the specified sequence of 32-bit limb words.

```csharp
public static GpuUInt512 Create(ReadOnlySpan<uint> elements);
```

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
A read-only span containing the 32-bit unsigned limbs in little-endian order.

#### Returns

_GpuUInt512_<br>
A new `GpuUInt512` instance containing the specified elements.

## Equals(GpuUInt512)

Indicates whether the current instance is equal to another `GpuUInt512` instance.

```csharp
public readonly bool Equals(GpuUInt512 other);
```

#### Parameters

`other` _GpuUInt512_<br>
A `GpuUInt512` to compare with this instance.

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
`true` if `obj` is a `GpuUInt512` and equals the current instance; otherwise, `false`.

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
| `Addition(GpuUInt512, GpuUInt512)` | Adds two `GpuUInt512` values. |
| `Subtraction(GpuUInt512, GpuUInt512)` | Subtracts one `GpuUInt512` value from another. |
| `Equality(GpuUInt512, GpuUInt512)` | Determines whether two `GpuUInt512` values are equal. |
| `Inequality(GpuUInt512, GpuUInt512)` | Determines whether two `GpuUInt512` values are not equal. |
| `LessThan(GpuUInt512, GpuUInt512)` | Determines whether one `GpuUInt512` value is less than another. |
| `GreaterThan(GpuUInt512, GpuUInt512)` | Determines whether one `GpuUInt512` value is greater than another. |
| `LessThanOrEqual(GpuUInt512, GpuUInt512)` | Determines whether one `GpuUInt512` value is less than or equal to another. |
| `GreaterThanOrEqual(GpuUInt512, GpuUInt512)` | Determines whether one `GpuUInt512` value is greater than or equal to another. |
| `Implicit(GpuUInt512 to GpuBigUInt<LimbBuffer16>)` | Implicitly converts a `GpuUInt512` to its underlying `GpuBigUInt<LimbBuffer16>` representation. |
| `Implicit(GpuBigUInt<LimbBuffer16> to GpuUInt512)` | Implicitly converts a `GpuBigUInt<LimbBuffer16>` value to a `GpuUInt512` instance. |
| `Implicit(uint to GpuUInt512)` | Implicitly converts a 32-bit unsigned integer to a `GpuUInt512` instance. |

## Examples

### Creating GpuUInt512 Instances

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU) or G# Kernel (GPU)

// 1. Create a 512-bit unsigned integer from a 32-bit scalar value
GpuUInt512 fromScalar = new GpuUInt512(42u);

// 2. Implicit conversion from uint is supported
GpuUInt512 implicitConversion = 100u;

// 3. Create from an array of sixteen 32-bit limbs (CollectionBuilder syntax)
GpuUInt512 fromLimbs = [0x00000001, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

// 4. Retrieve the underlying GpuBigUInt<LimbBuffer16> implicitly
GpuBigUInt<LimbBuffer16> underlyingBuffer = fromLimbs;

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
