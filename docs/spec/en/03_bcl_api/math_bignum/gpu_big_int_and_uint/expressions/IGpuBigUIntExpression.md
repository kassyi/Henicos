---
title: "IGpuBigUIntExpression Interface (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated GPU BigInteger expression."
---

## IGpuBigUIntExpression Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated unsigned GPU BigInteger expression.

```csharp
public interface IGpuBigUIntExpression
```

## Remarks

`IGpuBigUIntExpression` implements the Visitor pattern via [`BigIntExpressionVisitor`](BigIntExpressionVisitor.md) to traverse expression trees and emit fused or optimized GPU kernel sequences without intermediate managed object allocations. Direct evaluation into a [`GpuBigUInt`](../GpuBigUInt.md) buffer is provided by the [`GpuBigUIntExpressionExtensions.Evaluate`](GpuBigUIntExpressionExtensions.md) extension method.

## Properties

| Name | Description |
| --- | --- |
| [NumWords](#numwords) | Gets the word length (32-bit limb count) resulting from this expression. |

## NumWords

Gets the word length (32-bit limb count) resulting from this expression.

```csharp
int NumWords { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Accept(BigIntExpressionVisitor)](#acceptbigintexpressionvisitor) | Accepts an expression visitor to evaluate the expression tree using a scoped device memory context. |

## Accept(BigIntExpressionVisitor)

Accepts an expression visitor to evaluate the expression tree using a scoped device memory context.

```csharp
ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor);
```

#### Parameters

`visitor` _[BigIntExpressionVisitor](BigIntExpressionVisitor.md)_<br>
The expression visitor (passed by `scoped ref`) responsible for evaluating intermediate AST nodes.

#### Returns

_`ScopedBigIntMemory`_<br>
A stack-scoped `ScopedBigIntMemory` instance holding the evaluated GPU memory slice.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
using var left = new GpuBigUInt(workspace, new uint[] { 500 });
using var right = new GpuBigUInt(workspace, new uint[] { 700 });

GpuBigUIntExpr leftExpr = left;
GpuBigUIntExpr rightExpr = right;
IGpuBigUIntExpression expression = (leftExpr + rightExpr).Node;

using var dest = new GpuBigUInt(workspace, 2);
GpuBigUInt result = expression.Evaluate(workspace, dest);

var resultData = new uint[1];
workspace.Provider.CopyDeviceToHost(resultData, result.DevicePointer, 1);

Console.WriteLine($"500 + 700 = {resultData[0]}");
```

## See also

### BCL API > Math Bignum

- [GpuBigUIntExpressionExtensions Class](GpuBigUIntExpressionExtensions.md)
- [BigIntExpressionVisitor Struct](BigIntExpressionVisitor.md)
- [GpuBigUIntExpr Struct](GpuBigUIntExpr.md)
- [Standard Math Library (Stdlib.Math)](../../../00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
