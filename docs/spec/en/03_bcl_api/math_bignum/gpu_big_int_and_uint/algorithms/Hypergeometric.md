---
title: "GpuHypergeometricPqt Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for Hypergeometric Series (Chudnovsky algorithm) components."
---

## GpuHypergeometricPqt Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

The `GpuHypergeometricPqt` class provides specialized primitives for evaluating hypergeometric series. Its primary use case in computational mathematics is the **Chudnovsky algorithm**, which is currently the fastest known method for calculating millions (or billions) of digits of $\pi$ (Pi).

## Binary Splitting

The algorithm relies on a technique called **Binary Splitting**, which recursively computes three terms: $P$, $Q$, and $T$.

- $P$: Product of numerators.
- $Q$: Product of denominators.
- $T$: Partial sum.

Because binary splitting is inherently a massive reduction tree, it is an ideal candidate for GPU acceleration.

## Properties

| Name | Description                            |
| ---- | -------------------------------------- |
| `P`  | Gets the P term (Numerator product).   |
| `Q`  | Gets the Q term (Denominator product). |
| `T`  | Gets the T term (Partial sum).         |

## Examples

### Orchestrating the Chudnovsky Algorithm

This example demonstrates the core recursive step (combining left and right branches of the tree) in a binary splitting algorithm orchestrated on the GPU.

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)

using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

// Initialize some placeholder values
var pLeft = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
var qLeft = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
var tLeft = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);

var pRight = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
var qRight = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
var tRight = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);

// The binary splitting combination formula:
// P_new = P_left * P_right
// Q_new = Q_left * Q_right
// T_new = (T_left * Q_right) + (P_left * T_right)

// 1. Build the Expression Trees
var pNewExpr = pLeft * pRight;
var qNewExpr = qLeft * qRight;
var tNewExpr = (tLeft * qRight) + (pLeft * tRight);

// 2. Evaluate concurrently using the Context pool
using var pNewDest = new GpuBigUInt(workspace, 8);
var pNew = pNewExpr.Evaluate(pNewDest);

using var qNewDest = new GpuBigUInt(workspace, 8);
var qNew = qNewExpr.Evaluate(qNewDest);

using var tNewDest = new GpuBigUInt(workspace, 8);
var tNew = tNewExpr.Evaluate(tNewDest);

Console.WriteLine($"pNew = {pNew.Value.ToBigInteger(pNew.IsNegative)}");
Console.WriteLine($"qNew = {qNew.Value.ToBigInteger(qNew.IsNegative)}");
Console.WriteLine($"tNew = {tNew.Value.ToBigInteger(tNew.IsNegative)}");
```

## Remarks

The `GpuHypergeometricPqt` structure is designed to hold the three values required for a single binary splitting frame on the GPU memory heap, preventing fragmentation.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [Arbitrary-Precision Floats (GpuBigFloat)](../../gpu_big_float/00_index.md)
