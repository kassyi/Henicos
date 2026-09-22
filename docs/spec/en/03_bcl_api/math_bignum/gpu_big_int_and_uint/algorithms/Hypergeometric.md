---
title: "GpuHypergeometricPqt Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for Hypergeometric Series (Chudnovsky algorithm) components."
---

## GpuHypergeometricPqt Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

The `GpuHypergeometricPqt` class provides specialized primitives for evaluating hypergeometric series. Its primary use case in computational mathematics is the **Chudnovsky algorithm**, which is currently the fastest known method for calculating millions (or billions) of digits of $\pi$ (Pi).

## Binary Splitting

The algorithm relies on a technique called **Binary Splitting**, which recursively computes three terms: $P$, $Q$, and $T$. 
- $P$: Product of numerators.
- $Q$: Product of denominators.
- $T$: Partial sum.

Because binary splitting is inherently a massive reduction tree, it is an ideal candidate for GPU acceleration.

## Properties

| Name | Description |
| --- | --- |
| `P` | Gets the P term (Numerator product). |
| `Q` | Gets the Q term (Denominator product). |
| `T` | Gets the T term (Partial sum). |

## Examples

### Orchestrating the Chudnovsky Algorithm

This example demonstrates the core recursive step (combining left and right branches of the tree) in a binary splitting algorithm orchestrated on the GPU.

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 524288); // Huge memory pool for Pi digits

// Assume we have computed the P, Q, T terms for the left and right halves of a recursive tree
var pLeft = new GpuBigInteger(...);
var qLeft = new GpuBigInteger(...);
var tLeft = new GpuBigInteger(...);

var pRight = new GpuBigInteger(...);
var qRight = new GpuBigInteger(...);
var tRight = new GpuBigInteger(...);

// The binary splitting combination formula:
// P_new = P_left * P_right
// Q_new = Q_left * Q_right
// T_new = (T_left * Q_right) + (P_left * T_right)

// 1. Build the Expression Trees
var pNewExpr = pLeft * pRight;
var qNewExpr = qLeft * qRight;
var tNewExpr = (tLeft * qRight) + (pLeft * tRight);

// 2. Evaluate concurrently using the Context pool
// The GPU will fuse these operations and reuse memory automatically
using var pNew = pNewExpr.Node.Evaluate(context, null);
using var qNew = qNewExpr.Node.Evaluate(context, null);
using var tNew = tNewExpr.Node.Evaluate(context, null);

// Continue recursively until the root is reached...
```

## Remarks

The `GpuHypergeometricPqt` structure is designed to hold the three values required for a single binary splitting frame on the GPU memory heap, preventing fragmentation.

## See also

### Related Algorithms
- [Matrix2x2](Matrix2x2.md)
- [DivRem](DivRem.md)
- [ModPow](ModPow.md)

### Integer Types & Architecture
- [GpuBigInteger](../GpuBigInteger.md)
- [GpuUIntBigInteger](../GpuUIntBigInteger.md)
- [GpuBigInteger and GpuUIntBigInteger Overview](../00_index.md)
- [GpuContext](../../gpu_context/GpuContext.md)
- [Arbitrary-Precision Math Overview](../../00_index.md)
- [Gpu BCL API Overview](../../../00_index.md)

### Guides
- [Stdlib Math and Lazy Evaluation Guide](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [Precision and Performance Guide](../../../../04_guides/05_precision_and_performance_guide.md)
