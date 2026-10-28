---
title: "IGpuBoolVectorExpression Interface (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents a deferred GPU vector expression yielding an array of boolean predicates."
---

## IGpuBoolVectorExpression Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a deferred GPU vector expression yielding an array of boolean predicates (`1` for `true` and `0` for `false` per element).

```csharp
public interface IGpuBoolVectorExpression : IDisposable
```

Implements: `IDisposable`

## Remarks

`IGpuBoolVectorExpression` nodes are produced by vectorized comparison and logical operators on [`GpuBigIntVectorLinqExtensions`](GpuBigIntVectorLinqExtensions.md) (such as `IsGreaterThan`, `IsEqualTo`, `And`, `Or`, and `Not`) and consumed by filter and quantifier operations (`Where`, `Any`, and `All`).

## Properties

| Name | Description |
| --- | --- |
| [Length](#length) | Gets the number of elements in the boolean vector expression. |

## Length

Gets the number of elements in the boolean vector expression.

```csharp
int Length { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Evaluate(GpuWorkspace)](#evaluategpuworkspace) | Evaluates the deferred boolean vector expression on the GPU. |

## Evaluate(GpuWorkspace)

Evaluates the deferred boolean vector expression on the GPU.

```csharp
GpuBigUIntArrayView Evaluate(GpuWorkspace workspace);
```

#### Parameters

`workspace` _[GpuWorkspace](../GpuWorkspace.md)_<br>
The GPU workspace memory context.

#### Returns

_[GpuBigUIntArrayView](../GpuBigUIntArrayView.md)_<br>
A borrowed view containing `1` for `true` and `0` for `false` at each element index. It is valid until the next evaluation or until the expression is disposed. The next evaluation overwrites the values of a result still held, without an error.

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
using var threshold = new GpuBigUInt(workspace, new uint[] { 10, 0 });

// Create an IGpuBoolVectorExpression via vectorized comparison
using IGpuBoolVectorExpression predicate = array.AsVectorizedEnumerable()
    .IsLessThan(new GpuBigInt(threshold, isNegative: false));

Console.WriteLine($"Predicate vector length: {predicate.Length}");

GpuBigUIntArrayView mask = predicate.Evaluate(workspace);
var maskWords = new uint[4];
workspace.Provider.CopyDeviceToHost(maskWords, mask.DevicePointer, 4);
Console.WriteLine($"Mask values (1=true, 0=false): [{string.Join(", ", maskWords)}]");
```

## See also

### BCL API > Math Bignum

- [GpuBigIntVectorLinqExtensions Class](GpuBigIntVectorLinqExtensions.md)
- [BoolVectorExpressions (IGpuBoolExpr & VectorCompareOperator)](BoolVectorExpressions.md)
- [IGpuBigIntVectorExpression Interface](IGpuBigIntVectorExpression.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
