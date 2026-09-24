---
title: "GpuUInt128 Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a 128-bit unsigned integer stored on the GPU."
---

## GpuUInt128 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents a 128-bit unsigned integer stored on the GPU.

```csharp
[CollectionBuilder(typeof(GpuUInt128), "Create")]
public struct GpuUInt128
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuUInt128(GpuUIntBigInteger&lt;LimbBuffer4&gt;)](#gpuuint128gpuuintbiginteger-limbbuffer4) | Initializes a new instance of the `GpuUInt128` struct. |
| [GpuUInt128(uint)](#gpuuint128uint) | Initializes a new instance of the `GpuUInt128` struct. |

## GpuUInt128(GpuUIntBigInteger&lt;LimbBuffer4&gt;)

Initializes a new instance of the `GpuUInt128` struct from an existing buffer.

```csharp
public GpuUInt128(GpuUIntBigInteger<LimbBuffer4> value);
```

#### Parameters

`value` _GpuUIntBigInteger&lt;LimbBuffer4&gt;_<br>
The underlying buffer value.

## GpuUInt128(uint)

Initializes a new instance of the `GpuUInt128` struct from a 32-bit unsigned integer.

```csharp
public GpuUInt128(uint value);
```

#### Parameters

`value` _uint_<br>
The scalar value to initialize.

## Methods

| Name | Description |
| --- | --- |
| [Create(ReadOnlySpan&lt;uint&gt;)](#createreadonlyspan-uint) | Creates a new instance of the `GpuUInt128` struct from a span of limbs. |

## Create(ReadOnlySpan&lt;uint&gt;)

Creates a new instance of the `GpuUInt128` struct from a span of limbs.

```csharp
public static GpuUInt128 Create(ReadOnlySpan<uint> elements);
```

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
The span of limb values.

#### Returns

_GpuUInt128_<br>
A newly created `GpuUInt128` struct.

## Operators

| Name | Description |
| --- | --- |
| `Implicit(GpuUInt128 to GpuUIntBigInteger<LimbBuffer4>)` | Implicitly converts a `GpuUInt128` to a `GpuUIntBigInteger<LimbBuffer4>`. |
| `Implicit(GpuUIntBigInteger<LimbBuffer4> to GpuUInt128)` | Implicitly converts a `GpuUIntBigInteger<LimbBuffer4>` to a `GpuUInt128`. |
| `Implicit(uint to GpuUInt128)` | Implicitly converts a `uint` to a `GpuUInt128`. |

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
