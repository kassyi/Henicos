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

## Examples

### Creating GpuUInt128 Instances

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU) or G# Kernel (GPU)

// 1. Create a 128-bit unsigned integer from a 32-bit scalar value
GpuUInt128 fromScalar = new GpuUInt128(42u);

// 2. Implicit conversion from uint is supported
GpuUInt128 implicitConversion = 100u;

// 3. Create from an array of four 32-bit limbs (CollectionBuilder syntax)
GpuUInt128 fromLimbs = [ 0x00000001, 0x00000000, 0x00000000, 0x00000000 ];

// 4. Retrieve the underlying GpuUIntBigInteger<LimbBuffer4> implicitly
GpuUIntBigInteger<LimbBuffer4> underlyingBuffer = fromLimbs;
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
