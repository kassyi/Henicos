---
title: "IGpuBigIntVectorExpression Interface (G#)"
site: "G# Documentation"
published: "2026-10-12T00:00:00.000Z"
language: "en"
description: "Defines an expression node that evaluates to a contiguous array of GPU big integers."
---

## IGpuBigIntVectorExpression Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines an expression AST node that evaluates to a contiguous array of GPU big integers (`GpuBigIntArray`).

```csharp
public interface IGpuBigIntVectorExpression : IDisposable
```

## Properties

| Name | Description |
| --- | --- |
| [Length](#length) | Gets the number of elements in the vector. |
| [WordsPerElement](#wordsperelement) | Gets the number of 32-bit limbs (words) per element. |

## Length

Gets the number of elements in the vector.

```csharp
int Length { get; }
```

## WordsPerElement

Gets the number of 32-bit limbs (words) per element.

```csharp
int WordsPerElement { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Evaluate(GpuWorkspace, Int32, Int32?, GpuBigUIntArrayView)](#evaluategpuworkspace-int32-int32-gpubiguintarrayview) | Evaluates the vectorized expression into a new or existing device array, optionally computing a slice. |

## Evaluate(GpuWorkspace, Int32, Int32?, GpuBigUIntArrayView)

Evaluates the vectorized expression into a new or existing device array, optionally computing only a sub-slice.

```csharp
GpuBigIntArrayView Evaluate(
    GpuWorkspace workspace,
    int elementOffset = 0,
    int? elementCount = null,
    GpuBigUIntArrayView targetResult = default);
```

#### Parameters

`workspace` _[GpuWorkspace](../GpuWorkspace.md)_<br>
The GPU workspace used for memory allocation and CUDA stream execution.

`elementOffset` _Int32_<br>
The starting element index to evaluate. Defaults to `0`.

`elementCount` _Int32?_<br>
The number of elements to evaluate. If `null`, evaluates to the end of the vector length.

`targetResult` _[GpuBigUIntArrayView](../GpuBigUIntArrayView.md)_<br>
An optional destination for the evaluated values, with the expression's `WordsPerElement` and at least `elementCount` elements; only the first `elementCount` are written. The default view means none (an array of 0 elements is a target); a `GpuBigUIntArray` converts to a view implicitly. The expression borrows it and never frees it. It may share memory with an input (the input array itself, or a shifted view of it): the node then computes into its own result buffers and copies the result into the target.

#### Returns

_[GpuBigIntArrayView](../GpuBigIntArrayView.md)_<br>
A borrowed view of exactly the evaluated length: the node's own result buffers (with the target's values if one was given) or the input array. It is valid until the next evaluation or until the expression is disposed; pass a target to keep the values (a view has no `Dispose` and cannot be stored in a field). The next evaluation overwrites the values of a result still held, without an error. A node grows its result buffers when an evaluation needs more elements than an earlier one.

#### Exceptions

`ArgumentException`<br>
`targetResult` has another number of words per element, or fewer elements than evaluated.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

using var array = new GpuBigIntArray(workspace, length: 4, wordsPerElement: 2);
using var scalar = new GpuBigUInt(workspace, new uint[] { 5, 0 });

// Build a lazy vector expression and evaluate a 2-element slice starting at index 1
using IGpuBigIntVectorExpression expr = array.AsVectorizedEnumerable()
    .SelectAdd(new GpuBigInt(scalar, isNegative: false));

GpuBigIntArrayView sliceResult = expr.Evaluate(workspace, elementOffset: 1, elementCount: 2);
Console.WriteLine($"Evaluated slice length: {sliceResult.Length}, WordsPerElement: {sliceResult.WordsPerElement}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntArray Struct](../GpuBigIntArray.md)
- [GpuBigIntVectorLinqExtensions Class](GpuBigIntVectorLinqExtensions.md)
- [IGpuBoolVectorExpression Interface](IGpuBoolVectorExpression.md)
