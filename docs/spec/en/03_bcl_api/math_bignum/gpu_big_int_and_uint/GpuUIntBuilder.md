---
title: "GpuBigUIntBuilder Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A shared generic builder for C# 12 Collection Expressions."
---

## GpuBigUIntBuilder Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A shared generic builder for C# 12 Collection Expressions.

```csharp
public static class GpuBigUIntBuilder
```

## Methods

| Name                                                                            | Description                                                                   |
| ------------------------------------------------------------------------------- | ----------------------------------------------------------------------------- |
| [Create&lt;TSize&gt;(ReadOnlySpan&lt;uint&gt;)](#createt-sizereadonlyspan-uint) | Creates a fixed-size GPU unsigned big integer buffer from a span of elements. |

## Create&lt;TSize&gt;(ReadOnlySpan&lt;uint&gt;)

Creates a fixed-size GPU unsigned big integer buffer from a span of elements.

```csharp
public static GpuBigUInt<TSize> Create<TSize>(ReadOnlySpan<uint> elements) where TSize : unmanaged;
```

#### Type Parameters

`TSize`<br>
The unmanaged type defining the length of the fixed buffer.

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
The elements to copy into the fixed-size buffer.

#### Returns

_GpuUIntBigInteger&lt;TSize&gt;_<br>
A newly allocated fixed-size GPU unsigned big integer buffer.

#### Exceptions

`ArgumentException`<br>
Thrown when the number of elements provided exceeds the capacity of the target fixed-size buffer.

## Examples

### Constructing Fixed-Size Buffers

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt;

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
