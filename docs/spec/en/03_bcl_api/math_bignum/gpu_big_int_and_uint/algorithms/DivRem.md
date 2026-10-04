---
title: "GpuBigIntDivRem.DivRem Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for calculating Quotient and Remainder simultaneously on the GPU."
---

## GpuBigIntDivRem.DivRem Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

When performing division (`/`) or modulo (`%`) on massive multi-precision integers, the computational cost is extremely high. Often, an algorithm requires both the quotient and the remainder simultaneously (e.g., base conversion, Euclidean algorithms).

`GpuBigIntDivRem` provides an optimized way to calculate and return both values in a single GPU dispatch, cutting execution time in half compared to calling `/` and `%` separately.

## Overloads

| Name                                                                        | Description                                         |
| --------------------------------------------------------------------------- | --------------------------------------------------- |
| [DivRem(GpuBigInt, GpuBigInt)](#divrem-gpubiginteger-gpubiginteger) | Computes the quotient and remainder of two numbers. |

## DivRem(GpuBigInt, GpuBigInt)

Returns a tuple of lazy-evaluated expression trees representing the Quotient and Remainder.

```csharp
public static void DivRem(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer);
```

#### Parameters

`dividend` _GpuBigInteger_<br>
The number to divide.

`divisor` _GpuBigInteger_<br>
The number to divide by.

#### Returns

_(GpuBigIntExpr Quotient, GpuBigIntExpr Remainder)_<br>
A tuple containing two AST nodes. Evaluating them produces the respective quotient and remainder.

## Examples

### Simultaneous Division and Remainder

```csharp
// Execution Context: Host (CPU)

using var workspace = new GpuWorkspace(1024);

var dividend = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, ...));
var divisor = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, ...));

// 1. Build the Expression Trees
var (quotientExpr, remainderExpr) = GpuBigIntDivRem.DivRem(dividend, divisor);

// 2. Evaluate both using the workspace pool
// The GPU executes the division kernel once and extracts both results.
using var quotient = quotientExpr.Node.Evaluate(workspace, null);
using var remainder = remainderExpr.Node.Evaluate(workspace, null);

Console.WriteLine($"Q: {quotient.ToBigInteger()}, R: {remainder.ToBigInteger()}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuBigInt and GpuBigUInt Operators (G#)](../Operators.md)
