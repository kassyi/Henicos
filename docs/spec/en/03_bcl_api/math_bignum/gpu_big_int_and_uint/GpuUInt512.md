---
title: "GpuUInt512 Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a 512-bit unsigned integer stored on the GPU."
---

## GpuUInt512 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents a 512-bit unsigned integer stored on the GPU.

```csharp
[CollectionBuilder(typeof(GpuUInt512), "Create")]
public struct GpuUInt512
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuUInt512(GpuUIntBigInteger&lt;LimbBuffer16&gt;)](#gpuuint512gpuuintbiginteger-limbbuffer16) | Initializes a new instance of the `GpuUInt512` struct. |
| [GpuUInt512(uint)](#gpuuint512uint) | Initializes a new instance of the `GpuUInt512` struct. |

## GpuUInt512(GpuUIntBigInteger&lt;LimbBuffer16&gt;)

Initializes a new instance of the `GpuUInt512` struct from an existing buffer.

```csharp
public GpuUInt512(GpuUIntBigInteger<LimbBuffer16> value);
```

#### Parameters

`value` _GpuUIntBigInteger&lt;LimbBuffer16&gt;_<br>
The underlying buffer value.

## GpuUInt512(uint)

Initializes a new instance of the `GpuUInt512` struct from a 32-bit unsigned integer.

```csharp
public GpuUInt512(uint value);
```

#### Parameters

`value` _uint_<br>
The scalar value to initialize.

## Methods

| Name | Description |
| --- | --- |
| [Create(ReadOnlySpan&lt;uint&gt;)](#createreadonlyspan-uint) | Creates a new instance of the `GpuUInt512` struct from a span of limbs. |

## Create(ReadOnlySpan&lt;uint&gt;)

Creates a new instance of the `GpuUInt512` struct from a span of limbs.

```csharp
public static GpuUInt512 Create(ReadOnlySpan<uint> elements);
```

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
The span of limb values.

#### Returns

_GpuUInt512_<br>
A newly created `GpuUInt512` struct.

## Operators

| Name | Description |
| --- | --- |
| `Implicit(GpuUInt512 to GpuUIntBigInteger<LimbBuffer16>)` | Implicitly converts a `GpuUInt512` to a `GpuUIntBigInteger<LimbBuffer16>`. |
| `Implicit(GpuUIntBigInteger<LimbBuffer16> to GpuUInt512)` | Implicitly converts a `GpuUIntBigInteger<LimbBuffer16>` to a `GpuUInt512`. |
| `Implicit(uint to GpuUInt512)` | Implicitly converts a `uint` to a `GpuUInt512`. |

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
