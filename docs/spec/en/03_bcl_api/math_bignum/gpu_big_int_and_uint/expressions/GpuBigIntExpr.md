---
title: "GpuBigIntExpr Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A value type wrapper that builds a lazy expression tree of GPU signed BigInteger arithmetic."
---

# GpuBigIntExpr Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

A value type wrapper that builds a lazy expression tree of GPU signed BigInteger arithmetic. Deferred sign resolution allows for robust evaluation of operations where signs depend on runtime magnitudes (e.g. addition of different signs).

## Properties

| Name          | Description                                            |
| ------------- | ------------------------------------------------------ |
| [Node](#node) | Gets the root AST node of this signed expression tree. |

## Node

Gets the root AST node of this signed expression tree.

```csharp
public IGpuBigIntExpression Node { get; }
```

## Methods

| Name                                                      | Description                                                                   |
| --------------------------------------------------------- | ----------------------------------------------------------------------------- |
| [Evaluate(GpuUIntBigInteger)](#evaluategpuuintbiginteger) | Evaluates this signed expression into the provided destination buffer.        |
| [Evaluate(GpuContext)](#evaluategpucontext)               | Evaluates this signed expression into a newly allocated temporary GPU buffer. |

## Evaluate(GpuUIntBigInteger)

Evaluates this signed expression into the provided destination buffer.

```csharp
public GpuBigInteger Evaluate(GpuUIntBigInteger destination);
```

#### Parameters

`destination` _GpuUIntBigInteger_<br>
The pre-allocated GPU buffer to store the evaluated magnitude.

#### Returns

_GpuBigInteger_<br>
A new `GpuBigInteger` instance wrapping the destination buffer with the evaluated sign.

## Evaluate(GpuContext)

Evaluates this signed expression into a newly allocated temporary GPU buffer.

```csharp
public GpuBigInteger Evaluate(GpuContext context);
```

#### Parameters

`context` _GpuContext_<br>
The GPU context used for temporary buffer allocation.

#### Returns

_GpuBigInteger_<br>
A new `GpuBigInteger` instance wrapping the temporary buffer with the evaluated sign.

## Operators

| Name                                             | Description                                                             |
| ------------------------------------------------ | ----------------------------------------------------------------------- |
| `implicit operator GpuBigIntExpr(GpuBigInteger)` | Implicitly wraps a `GpuBigInteger` as a signed expression leaf node.    |
| `operator *(GpuBigIntExpr, GpuBigIntExpr)`       | Constructs a lazy multiplication expression.                            |
| `operator /(GpuBigIntExpr, GpuBigIntExpr)`       | Constructs a lazy division expression.                                  |
| `operator +(GpuBigIntExpr, GpuBigIntExpr)`       | Constructs a lazy addition expression with deferred sign resolution.    |
| `operator -(GpuBigIntExpr, GpuBigIntExpr)`       | Constructs a lazy subtraction expression with deferred sign resolution. |
| `operator -(GpuBigIntExpr)`                      | Constructs a lazy negation expression.                                  |
| `operator ~(GpuBigIntExpr)`                      | Constructs a lazy bitwise NOT expression.                               |

## Remarks

This struct serves as the frontend for operator overloading on GPU BigIntegers, avoiding memory allocations by building a lightweight AST. The expression tree is then evaluated lazily in a single pass on the GPU device.

## Examples

### Building and Evaluating AST on GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System.Numerics;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

// Execution Context: Host (CPU)
// Allocate a GPU execution context with 1024 words (4KB) of memory pool.
using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

// Upload initial buffers to the GPU device
using var uintA = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(100), minWords: 4);
using var uintB = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(200), minWords: 4);

var a = new GpuBigInteger(uintA, isNegative: false);
var b = new GpuBigInteger(uintB, isNegative: true);

// Build AST node lazy expression wrapper
GpuBigIntExpr exprA = a;
GpuBigIntExpr exprB = b;

// Construct AST tree without executing computation
var sumExpr = exprA + exprB;
var prodExpr = sumExpr * exprA;

// Evaluate AST back to a magnitude buffer
using var dest = new GpuUIntBigInteger(context, 8);
var finalResult = prodExpr.Evaluate(dest);

Console.WriteLine($"Result={finalResult.Value.ToBigInteger(finalResult.IsNegative)}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../../00_index.md)
- [Arbitrary-Precision Integers (GpuBigInteger)](../00_index.md)
