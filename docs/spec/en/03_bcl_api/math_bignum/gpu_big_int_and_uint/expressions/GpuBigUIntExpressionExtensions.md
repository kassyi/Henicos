---
title: "GpuBigUIntExpressionExtensions Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides extension methods for evaluating unsigned GPU BigInteger AST expressions using the Visitor pattern."
---

## GpuBigUIntExpressionExtensions Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides extension methods for evaluating unsigned GPU BigInteger AST expressions (`IGpuBigUIntExpression`) on the GPU using the stack-scoped `BigIntExpressionVisitor`.

```csharp
public static class GpuBigUIntExpressionExtensions
```

Inheritance: `Object` $\rightarrow$ `GpuBigUIntExpressionExtensions`

## Methods

| Name | Description |
| --- | --- |
| [Evaluate(IGpuBigUIntExpression, GpuWorkspace, GpuBigUInt?)](#evaluateigpubiguintexpression-gpuworkspace-gpubiguint) | Evaluates an arbitrary-precision unsigned integer expression AST on the GPU into a new or provided target buffer using the Visitor pattern. |

## Evaluate(IGpuBigUIntExpression, GpuWorkspace, GpuBigUInt?)

Evaluates an arbitrary-precision unsigned integer expression AST on the GPU into a new or provided target buffer using the Visitor pattern.

```csharp
public static GpuBigUInt Evaluate(
    this IGpuBigUIntExpression expr,
    GpuWorkspace workspace,
    GpuBigUInt? targetResult = null);
```

#### Parameters

`expr` _[IGpuBigUIntExpression](IGpuBigUIntExpression.md)_<br>
The root expression AST node to evaluate.

`workspace` _[GpuWorkspace](../GpuWorkspace.md)_<br>
The GPU workspace managing kernel parameters and memory pools.

`targetResult` _[GpuBigUInt](../GpuBigUInt.md)?_<br>
An optional destination buffer. If `null`, a temporary buffer is rented from `workspace`. It may be one of the expression's operands (`x.Value = x / y`): the root then computes into a temporary and copies the result into it.

#### Returns

_[GpuBigUInt](../GpuBigUInt.md)_<br>
A `GpuBigUInt` containing the evaluated unsigned large integer.

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

using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
using var a = GpuBigUInt.FromBigInteger(workspace, new BigInteger(1200), minWords: 2);
using var b = GpuBigUInt.FromBigInteger(workspace, new BigInteger(340), minWords: 2);

// Build a lazy unsigned expression AST and evaluate its IGpuBigUIntExpression Node
GpuBigUIntExpr exprA = a;
GpuBigUIntExpr exprB = b;
IGpuBigUIntExpression expr = (exprA + exprB).Node;

using var dest = new GpuBigUInt(workspace, 4);
GpuBigUInt result = expr.Evaluate(workspace, dest);

Console.WriteLine($"1200 + 340 = {result.ToBigInteger(false)}");
```

## See also

### BCL API > Math Bignum

- [IGpuBigUIntExpression Interface](IGpuBigUIntExpression.md)
- [GpuBigUIntExpr Struct](GpuBigUIntExpr.md)
- [BigIntExpressionVisitor Struct](BigIntExpressionVisitor.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
