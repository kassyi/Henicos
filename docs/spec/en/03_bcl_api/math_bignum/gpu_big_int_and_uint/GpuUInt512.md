---
title: "GpuUInt512 Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a 512-bit unsigned integer stored on the GPU."
---

## GpuUInt512 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a 512-bit unsigned integer stored on the GPU.

```csharp
[CollectionBuilder(typeof(GpuUInt512), "Create")]
public struct GpuUInt512
```

## Constructors

| Name                                                                                           | Description                                            |
| ---------------------------------------------------------------------------------------------- | ------------------------------------------------------ |
| [GpuUInt512(GpuBigUInt&lt;LimbBuffer16&gt;)](#gpuuint512gpuuintbiginteger-limbbuffer16) | Initializes a new instance of the `GpuUInt512` struct. |
| [GpuUInt512(uint)](#gpuuint512uint)                                                            | Initializes a new instance of the `GpuUInt512` struct. |

## GpuUInt512(GpuBigUInt&lt;LimbBuffer16&gt;)

Initializes a new instance of the `GpuUInt512` struct from an existing buffer.

```csharp
public GpuUInt512(GpuBigUInt<LimbBuffer16> value);
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

| Name                                                         | Description                                                             |
| ------------------------------------------------------------ | ----------------------------------------------------------------------- |
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

| Name                                                      | Description                                                                |
| --------------------------------------------------------- | -------------------------------------------------------------------------- |
| `Implicit(GpuUInt512 to GpuBigUInt<LimbBuffer16>)` | Implicitly converts a `GpuUInt512` to a `GpuBigUInt<LimbBuffer16>`. |
| `Implicit(GpuBigUInt<LimbBuffer16> to GpuUInt512)` | Implicitly converts a `GpuBigUInt<LimbBuffer16>` to a `GpuUInt512`. |
| `Implicit(uint to GpuUInt512)`                            | Implicitly converts a `uint` to a `GpuUInt512`.                            |

## Examples

### Creating GpuUInt512 Instances

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU) or G# Kernel (GPU)

// 1. Create a 512-bit unsigned integer from a 32-bit scalar value
GpuUInt512 fromScalar = new GpuUInt512(42u);

// 2. Implicit conversion from uint is supported
GpuUInt512 implicitConversion = 100u;

// 3. Create from an array of sixteen 32-bit limbs (CollectionBuilder syntax)
GpuUInt512 fromLimbs = [ 0x00000001, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ];

// 4. Retrieve the underlying GpuBigUInt<LimbBuffer16> implicitly
GpuBigUInt<LimbBuffer16> underlyingBuffer = fromLimbs;

Console.WriteLine($"fromScalar.Value.Limbs[0] = {fromScalar.Value.Limbs[0]}");
Console.WriteLine($"implicitConversion.Value.Limbs[0] = {implicitConversion.Value.Limbs[0]}");
Console.WriteLine($"fromLimbs.Value.Limbs[0] = {fromLimbs.Value.Limbs[0]}");
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
