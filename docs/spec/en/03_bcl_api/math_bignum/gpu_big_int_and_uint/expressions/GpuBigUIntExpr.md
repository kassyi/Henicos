---
title: "GpuBigUIntExpr Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A value type wrapper that builds a lazy expression tree of GPU BigInteger arithmetic and bitwise operations."
---

## GpuBigUIntExpr Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A value type wrapper that builds a lazy expression tree of GPU BigInteger arithmetic and bitwise operations.

```csharp
public readonly struct GpuBigUIntExpr
```

## Properties

| Name | Description |
| --- | --- |
| [Node](#node) | Gets the root AST node of this expression tree. |

## Node

Gets the root AST node of this expression tree.

```csharp
public IGpuBigUIntExpression Node { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Fit(int)](#fitint) | Narrows the expression to a word count, checked: a value that does not fit is a domain error. |
| [Sqrt(GpuBigUIntExpr)](#sqrtgpubiguintexpr) | Constructs a lazy square root expression. |
| [Truncate(int)](#truncateint) | Narrows the expression to a word count, unchecked: the value modulo `2^(32 * words)`. |

## Fit(int)

Narrows this expression to `words` words, checked: a value that does not fit is a domain error, as a C# `checked` narrowing. On the GPU the error is reported as `OverflowException` at the next read of the workspace stream (or by `GpuWorkspace.ThrowIfDeviceErrors`); in CPU fallback mode it is thrown when the expression is evaluated.

```csharp
public GpuBigUIntExpr Fit(int words);
```

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown if `words` is below 1.

## Truncate(int)

Narrows this expression to `words` words, unchecked: the value modulo `2^(32 * words)`, as a C# `unchecked` narrowing. A wider word count zero-extends.

```csharp
public GpuBigUIntExpr Truncate(int words);
```

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown if `words` is below 1.

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

| Name | Description |
| --- | --- |
| `implicit operator GpuBigUIntExpr(GpuBigUInt)` | Implicitly wraps a `GpuBigUInt` buffer as an expression leaf node. |
| `operator +(GpuBigUIntExpr, GpuBigUIntExpr)` | Constructs a lazy addition expression. |
| `operator -(GpuBigUIntExpr, GpuBigUIntExpr)` | Constructs a lazy subtraction expression; a difference below zero is a domain error. |
| `operator *(GpuBigUIntExpr, GpuBigUIntExpr)` | Constructs a lazy multiplication expression. |
| `operator /(GpuBigUIntExpr, GpuBigUIntExpr)` | Constructs a lazy division expression. |
| `operator &(GpuBigUIntExpr, GpuBigUIntExpr)` | Constructs a lazy bitwise AND expression. |
| `operator \|(GpuBigUIntExpr, GpuBigUIntExpr)` | Constructs a lazy bitwise OR expression. |
| `operator ^(GpuBigUIntExpr, GpuBigUIntExpr)` | Constructs a lazy bitwise XOR expression. |
| `operator ~(GpuBigUIntExpr)` | Constructs a lazy bitwise NOT expression. |
| `operator <<(GpuBigUIntExpr, int)` | Constructs a lazy left shift expression that keeps every bit; a negative count shifts right, as `BigInteger`. |
| `operator >>(GpuBigUIntExpr, int)` | Constructs a lazy right shift expression; a negative count shifts left, as `BigInteger`. |

## Remarks

**Result widths:** a result never overflows. Every node has an upper bound of its result in bits, computed from its operands when the expression is built, and the result has the words of that bound (`Node.NumWords`): `a + b` one bit more than the wider operand, `a - b` the left operand, `a * b` the sum of the operands, `a / b` the dividend, `a % b` and `a & b` the narrower operand, `a | b` and `a ^ b` the wider one, `a << k` `k` bits more, `a >> k` `k` bits fewer (at least one), `~a` every bit of the words of `a`, the square root half the bits. The operands may have different widths. A target must be at least as wide as the result; its words above the result are cleared. Narrow a result explicitly with `Fit` or `Truncate`.

**Domain errors:** a difference below zero, and a value that does not `Fit`, are reported as `OverflowException` at the next read of the workspace stream (or by `GpuWorkspace.ThrowIfDeviceErrors`); in CPU fallback mode they are thrown at the evaluation.

This struct serves as the frontend for operator overloading on GPU Unsigned BigIntegers, avoiding intermediate allocations by building a lightweight AST. The evaluation rents memory from the `GpuWorkspace` buffer pool transparently.

## Examples

### Building and Evaluating AST on GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
// Allocate a GPU workspace with 1024 words (4KB) of memory pool.
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

// Upload initial buffers to the GPU device
using var uintA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(100), minWords: 4);
using var uintB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(200), minWords: 4);

// Build AST node lazy expression wrapper
GpuBigUIntExpr exprA = uintA;
GpuBigUIntExpr exprB = uintB;

// Construct AST tree without executing computation
var sumExpr = exprA + exprB;
var prodExpr = sumExpr * exprA;

// Evaluate AST back to a magnitude buffer as wide as the result: (4 words + 1 bit) * 4 words needs 9 words
using var dest = new GpuBigUInt(workspace, prodExpr.Node.NumWords);
var finalResult = prodExpr.Node.Evaluate(workspace, dest);

Console.WriteLine($"Result={finalResult.ToBigInteger(false)}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../../00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
