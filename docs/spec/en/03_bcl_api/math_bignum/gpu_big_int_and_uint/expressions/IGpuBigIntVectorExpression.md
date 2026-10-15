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
| [Evaluate(GpuWorkspace, Int32, Int32?, GpuBigUIntArray?)](#evaluategpuworkspace-int32-int32-gpubiguintarray) | Evaluates the vectorized expression into a new or existing device array, optionally computing a slice. |

## Evaluate(GpuWorkspace, Int32, Int32?, GpuBigUIntArray?)

Evaluates the vectorized expression into a new or existing device array, optionally computing only a sub-slice.

```csharp
GpuBigIntArray Evaluate(
    GpuWorkspace workspace,
    int elementOffset = 0,
    int? elementCount = null,
    GpuBigUIntArray? targetResult = null);
```

#### Parameters

`workspace` _[GpuWorkspace](../GpuWorkspace.md)_<br>
The GPU workspace used for memory allocation and CUDA stream execution.

`elementOffset` _Int32_<br>
The starting element index to evaluate. Defaults to `0`.

`elementCount` _Int32?_<br>
The number of elements to evaluate. If `null`, evaluates to the end of the vector length.

`targetResult` _[GpuBigUIntArray](../GpuBigUIntArray.md)?_<br>
An optional destination array for the evaluated values. If provided, it must have a length of `elementCount`.

#### Returns

_[GpuBigIntArray](../GpuBigIntArray.md)_<br>
A `GpuBigIntArray` containing the evaluated vector slice results.

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

using GpuBigIntArray sliceResult = expr.Evaluate(workspace, elementOffset: 1, elementCount: 2);
Console.WriteLine($"Evaluated slice length: {sliceResult.Length}, WordsPerElement: {sliceResult.WordsPerElement}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntArray Struct](../GpuBigIntArray.md)
- [GpuBigIntVectorLinqExtensions Class](GpuBigIntVectorLinqExtensions.md)
- [IGpuBoolVectorExpression Interface](IGpuBoolVectorExpression.md)
