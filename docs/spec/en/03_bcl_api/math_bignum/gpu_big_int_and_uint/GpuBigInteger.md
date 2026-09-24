---
title: "GpuBigInteger Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A lightweight value type wrapping a GpuUIntBigInteger with a sign bit, used for tracking alternating series or arbitrary signed integers on the GPU."
---

## GpuBigInteger Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

A lightweight value type wrapping a `GpuUIntBigInteger` with a sign bit, used for tracking alternating series or arbitrary signed integers on the GPU.

```csharp
public readonly struct GpuBigInteger : IDisposable, IEquatable<GpuBigInteger>, IComparable<GpuBigInteger>
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigInteger(GpuUIntBigInteger, bool)](#gpubigintegergpuuintbiginteger-bool) | Initializes a new instance of the `GpuBigInteger` struct. |

## GpuBigInteger(GpuUIntBigInteger, bool)

Initializes a new instance of the `GpuBigInteger` struct.

```csharp
public GpuBigInteger([TakesOwnership] GpuUIntBigInteger value, bool isNegative);
```

#### Parameters

`value` _GpuUIntBigInteger_<br>
The underlying unsigned integer value.

`isNegative` _bool_<br>
Indicates whether the value is strictly negative.

## Properties

| Name | Description |
| --- | --- |
| [IsNegative](#isnegative) | Gets a value indicating whether the integer is strictly negative. |
| [Value](#value) | Gets the underlying absolute value buffer on the GPU. |

## IsNegative

Gets a value indicating whether the integer is strictly negative.

```csharp
public bool IsNegative { get; }
```

## Value

Gets the underlying absolute value buffer on the GPU.

```csharp
public GpuUIntBigInteger Value { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [CompareTo(GpuBigInteger)](#comparetogpubiginteger) | Compares this instance with another `GpuBigInteger` and returns an integer that indicates whether this instance precedes, follows, or appears in the same position in the sort order as the specified parameter. |
| [Dispose()](#dispose) | Disposes the underlying GPU buffer. |
| [Equals(GpuBigInteger)](#equalsgpubiginteger) | Indicates whether the current object is equal to another object of the same type. |
| [Equals(object?)](#equalsobject) | Indicates whether this instance and a specified object are equal. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |

## CompareTo(GpuBigInteger)

Compares this instance with another `GpuBigInteger` and returns an integer that indicates whether this instance precedes, follows, or appears in the same position in the sort order as the specified parameter.

```csharp
public int CompareTo(GpuBigInteger other);
```

#### Parameters

`other` _GpuBigInteger_<br>
An object to compare with this instance.

#### Returns

_int_<br>
A value that indicates the relative order of the objects being compared.

## Dispose()

Disposes the underlying GPU buffer.

```csharp
public void Dispose();
```

## Equals(GpuBigInteger)

Indicates whether the current object is equal to another object of the same type.

```csharp
public bool Equals(GpuBigInteger other);
```

#### Parameters

`other` _GpuBigInteger_<br>
An object to compare with this object.

#### Returns

_bool_<br>
`true` if the current object is equal to the `other` parameter; otherwise, `false`.

## Equals(object?)

Indicates whether this instance and a specified object are equal.

```csharp
public override bool Equals(object? obj);
```

#### Parameters

`obj` _object?_<br>
The object to compare with the current instance.

#### Returns

_bool_<br>
`true` if `obj` and this instance are the same type and represent the same value; otherwise, `false`.

## GetHashCode()

Returns the hash code for this instance.

```csharp
public override int GetHashCode();
```

#### Returns

_int_<br>
A 32-bit signed integer that is the hash code for this instance.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuBigInteger, GpuBigInteger)` | Adds two big integers. |
| `Subtraction(GpuBigInteger, GpuBigInteger)` | Subtracts one big integer from another. |
| `Multiply(GpuBigInteger, GpuBigInteger)` | Multiplies two big integers. |
| `Division(GpuBigInteger, GpuBigInteger)` | Divides one big integer by another. |
| `Equality(GpuBigInteger, GpuBigInteger)` | Indicates whether two `GpuBigInteger` values are equal. |
| `Inequality(GpuBigInteger, GpuBigInteger)` | Indicates whether two `GpuBigInteger` values are not equal. |
| `LessThan(GpuBigInteger, GpuBigInteger)` | Returns a value indicating whether a specified `GpuBigInteger` is less than another specified `GpuBigInteger`. |
| `GreaterThan(GpuBigInteger, GpuBigInteger)` | Returns a value indicating whether a specified `GpuBigInteger` is greater than another specified `GpuBigInteger`. |
| `LessThanOrEqual(GpuBigInteger, GpuBigInteger)` | Returns a value indicating whether a specified `GpuBigInteger` is less than or equal to another specified `GpuBigInteger`. |
| `GreaterThanOrEqual(GpuBigInteger, GpuBigInteger)` | Returns a value indicating whether a specified `GpuBigInteger` is greater than or equal to another specified `GpuBigInteger`. |

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
