---
title: "GpuBigInteger.Gcd Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for calculating the Greatest Common Divisor (GCD) using Lehmer's algorithm on the GPU."
---

## GpuBigInteger.Gcd Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Calculates the Greatest Common Divisor (GCD) of two arbitrary-precision integers using Lehmer's algorithm.

## Overloads

| Name | Description |
| --- | --- |
| [Gcd(GpuBigInteger, GpuBigInteger)](#gcd-gpubiginteger-gpubiginteger) | Computes the greatest common divisor of two values. |

## Gcd(GpuBigInteger, GpuBigInteger)

Builds a lazy-evaluated expression tree node for calculating the Greatest Common Divisor.

```csharp
public static GpuBigIntExpr Gcd(GpuBigInteger left, GpuBigInteger right);
```

#### Parameters

`left` _GpuBigInteger_<br>
The first value.

`right` _GpuBigInteger_<br>
The second value.

#### Returns

_GpuBigIntExpr_<br>
An expression tree node that, when evaluated, produces the greatest common divisor of the two inputs.

## Examples

### Calculating GCD of massive numbers

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(1024);

var aVal = GpuUIntBigInteger.FromBigInteger(context, BigInteger.Parse("..."));
var bVal = GpuUIntBigInteger.FromBigInteger(context, BigInteger.Parse("..."));

var gpuA = new GpuBigInteger(aVal);
var gpuB = new GpuBigInteger(bVal);

// Build the lazy GCD expression
var gcdExpr = GpuBigInteger.Gcd(gpuA, gpuB);

// Evaluate it using the context's memory pool
using var result = gcdExpr.Node.Evaluate(context, null);

Console.WriteLine($"GCD Result: {result.ToBigInteger()}");
```

## Remarks

This method implements Lehmer's GCD algorithm. It relies heavily on 64-bit multi-word arithmetic on the GPU. Because this operation requires substantial temporary memory for intermediate divisions, it uses the `GpuContext` memory pool instead of `cudaMalloc`.

## See also

### Related Algorithms
- [DivRem](DivRem.md)
- [ModPow](ModPow.md)
- [Matrix2x2](Matrix2x2.md)

### Integer Types & Architecture
- [GpuBigInteger](../GpuBigInteger.md)
- [GpuUIntBigInteger](../GpuUIntBigInteger.md)
- [GpuBigInteger and GpuUIntBigInteger Overview](../00_overview.md)
- [GpuContext](../../gpu_context/GpuContext.md)
- [Arbitrary-Precision Math Overview](../../00_overview.md)
- [Gpu BCL API Overview](../../../00_overview.md)

### Guides
- [Stdlib Math and Lazy Evaluation Guide](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [Antipatterns and Best Practices Guide (Underflow Traps)](../../../../04_guides/06_antipatterns_and_best_practices.md)
