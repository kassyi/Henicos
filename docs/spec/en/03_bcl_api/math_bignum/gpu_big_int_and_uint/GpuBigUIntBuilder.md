---
title: "GpuBigUIntBuilder Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A factory builder facilitating C# 12 Collection Expression construction for GpuBigUInt<TSize>."
---

## GpuBigUIntBuilder Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A factory builder facilitating C# 12 Collection Expression construction for `GpuBigUInt<TSize>`.

```csharp
public static class GpuBigUIntBuilder
```

## Methods

| Name | Description |
| --- | --- |
| [Create&lt;TSize&gt;(ReadOnlySpan&lt;uint&gt;)](#createtsizereadonlyspanuint) | Creates a new `GpuBigUInt<TSize>` instance initialized from a span of 32-bit unsigned limbs. |

## Create&lt;TSize&gt;(ReadOnlySpan&lt;uint&gt;)

Creates a new `GpuBigUInt<TSize>` instance initialized from a span of 32-bit unsigned limbs.

```csharp
public static GpuBigUInt<TSize> Create<TSize>(ReadOnlySpan<uint> elements) where TSize : unmanaged;
```

#### Type Parameters

`TSize`<br>
The unmanaged struct type representing the limb storage layout.

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
A read-only span containing the 32-bit unsigned limbs in little-endian order.

#### Returns

_GpuBigUInt&lt;TSize&gt;_<br>
A new `GpuBigUInt<TSize>` instance containing the specified limbs.

#### Exceptions

`ArgumentException`<br>
Thrown when `elements` length exceeds the capacity of `TSize`.

## Examples

### Constructing Fixed-Size Buffers

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU) or G# Kernel (GPU)
// GpuBigUIntBuilder provides the infrastructure for C# 12 collection expressions.
// It allows constructing fixed-size GpuBigUInt<TSize> instances elegantly.
GpuBigUInt<LimbBuffer4> fromLimbs = GpuBigUIntBuilder.Create<LimbBuffer4>([1, 2, 3, 4]);

Console.WriteLine($"fromLimbs.Limbs[0] = {fromLimbs.Limbs[0]}");
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
