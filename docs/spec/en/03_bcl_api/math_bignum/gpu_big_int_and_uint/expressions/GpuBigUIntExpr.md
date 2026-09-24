---
title: "GpuBigUIntExpr Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A value type wrapper that builds a lazy expression tree of GPU BigInteger arithmetic and bitwise operations."
---

# GpuBigUIntExpr Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

A value type wrapper that builds a lazy expression tree of GPU BigInteger arithmetic and bitwise operations.

## Properties

| Name          | Description                                     |
| ------------- | ----------------------------------------------- |
| [Node](#node) | Gets the root AST node of this expression tree. |

## Node

Gets the root AST node of this expression tree.

```csharp
public IGpuBigUIntExpression Node { get; }
```

## Methods

| Name                                        | Description                               |
| ------------------------------------------- | ----------------------------------------- |
| [Sqrt(GpuBigUIntExpr)](#sqrtgpubiguintexpr) | Constructs a lazy square root expression. |

## Sqrt(GpuBigUIntExpr)

Constructs a lazy square root expression.

```csharp
public static GpuBigUIntExpr Sqrt(GpuBigUIntExpr expr);
```

#### Parameters

`expr` _GpuBigUIntExpr_<br>
The expression to compute the square root for.

#### Returns

_GpuBigUIntExpr_<br>
A lazy AST node representing the square root.

## Operators

| Name                                                  | Description                                                               |
| ----------------------------------------------------- | ------------------------------------------------------------------------- |
| `implicit operator GpuBigUIntExpr(GpuUIntBigInteger)` | Implicitly wraps a `GpuUIntBigInteger` buffer as an expression leaf node. |
| `operator +(GpuBigUIntExpr, GpuBigUIntExpr)`          | Constructs a lazy addition expression.                                    |
| `operator -(GpuBigUIntExpr, GpuBigUIntExpr)`          | Constructs a lazy subtraction expression.                                 |
| `operator *(GpuBigUIntExpr, GpuBigUIntExpr)`          | Constructs a lazy multiplication expression.                              |
| `operator /(GpuBigUIntExpr, GpuBigUIntExpr)`          | Constructs a lazy division expression.                                    |
| `operator &(GpuBigUIntExpr, GpuBigUIntExpr)`          | Constructs a lazy bitwise AND expression.                                 |
| `operator \|(GpuBigUIntExpr, GpuBigUIntExpr)`         | Constructs a lazy bitwise OR expression.                                  |
| `operator ^(GpuBigUIntExpr, GpuBigUIntExpr)`          | Constructs a lazy bitwise XOR expression.                                 |
| `operator ~(GpuBigUIntExpr)`                          | Constructs a lazy bitwise NOT expression.                                 |
| `operator <<(GpuBigUIntExpr, int)`                    | Constructs a lazy left shift expression.                                  |
| `operator >>(GpuBigUIntExpr, int)`                    | Constructs a lazy right shift expression.                                 |

## Remarks

This struct serves as the frontend for operator overloading on GPU Unsigned BigIntegers, avoiding intermediate allocations by building a lightweight AST. The evaluation rents memory from the `GpuContext` buffer pool transparently.

## Examples

### Building and Evaluating AST on GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

// Execution Context: Host (CPU)
// Allocate a GPU execution context with 1024 words (4KB) of memory pool.
using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

// Upload initial buffers to the GPU device
using var uintA = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(100), minWords: 4);
using var uintB = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(200), minWords: 4);

// Build AST node lazy expression wrapper
GpuBigUIntExpr exprA = uintA;
GpuBigUIntExpr exprB = uintB;

// Construct AST tree without executing computation
var sumExpr = exprA + exprB;
var prodExpr = sumExpr * exprA;

// Evaluate AST back to a magnitude buffer
using var dest = new GpuUIntBigInteger(context, 8);
var finalResult = prodExpr.Node.Evaluate(context, dest);

Console.WriteLine($"Result={finalResult.ToBigInteger(false)}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../../00_index.md)
- [Arbitrary-Precision Integers (GpuBigInteger)](../00_index.md)
