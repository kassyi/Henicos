---
title: "GpuUInt256 Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a 256-bit unsigned integer stored on the GPU."
---

## GpuUInt256 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents a 256-bit unsigned integer stored on the GPU.

```csharp
[CollectionBuilder(typeof(GpuUInt256), "Create")]
public struct GpuUInt256
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuUInt256(GpuUIntBigInteger&lt;LimbBuffer8&gt;)](#gpuuint256gpuuintbiginteger-limbbuffer8) | Initializes a new instance of the `GpuUInt256` struct. |
| [GpuUInt256(uint)](#gpuuint256uint) | Initializes a new instance of the `GpuUInt256` struct. |

## GpuUInt256(GpuUIntBigInteger&lt;LimbBuffer8&gt;)

Initializes a new instance of the `GpuUInt256` struct from an existing buffer.

```csharp
public GpuUInt256(GpuUIntBigInteger<LimbBuffer8> value);
```

#### Parameters

`value` _GpuUIntBigInteger&lt;LimbBuffer8&gt;_<br>
The underlying buffer value.

## GpuUInt256(uint)

Initializes a new instance of the `GpuUInt256` struct from a 32-bit unsigned integer.

```csharp
public GpuUInt256(uint value);
```

#### Parameters

`value` _uint_<br>
The scalar value to initialize.

## Methods

| Name | Description |
| --- | --- |
| [Create(ReadOnlySpan&lt;uint&gt;)](#createreadonlyspan-uint) | Creates a new instance of the `GpuUInt256` struct from a span of limbs. |

## Create(ReadOnlySpan&lt;uint&gt;)

Creates a new instance of the `GpuUInt256` struct from a span of limbs.

```csharp
public static GpuUInt256 Create(ReadOnlySpan<uint> elements);
```

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
The span of limb values.

#### Returns

_GpuUInt256_<br>
A newly created `GpuUInt256` struct.

## Operators

| Name | Description |
| --- | --- |
| `Implicit(GpuUInt256 to GpuUIntBigInteger<LimbBuffer8>)` | Implicitly converts a `GpuUInt256` to a `GpuUIntBigInteger<LimbBuffer8>`. |
| `Implicit(GpuUIntBigInteger<LimbBuffer8> to GpuUInt256)` | Implicitly converts a `GpuUIntBigInteger<LimbBuffer8>` to a `GpuUInt256`. |
| `Implicit(uint to GpuUInt256)` | Implicitly converts a `uint` to a `GpuUInt256`. |

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
