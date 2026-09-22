---
title: "GpuBigInteger.NttMultiply Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for the Number Theoretic Transform (NTT) for O(N log N) multiplication."
---

## GpuBigInteger.NttMultiply Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

When multiplying two numbers, the standard "schoolbook" multiplication algorithm operates in $O(N^2)$ time. This is acceptable for numbers up to a few thousand bits. However, when multiplying numbers with **millions of bits**, $O(N^2)$ becomes impossibly slow.

To solve this, GSharp provides an ultra-optimized **Number Theoretic Transform (NTT)** implementation. NTT is a discrete Fourier transform over a finite field. It reduces the time complexity of multiplication from $O(N^2)$ to $O(N \log N)$ by converting the multiplication into a convolution in the frequency domain.

## Overloads

| Name | Description |
| --- | --- |
| [NttMultiply(GpuBigInteger, GpuBigInteger)](#nttmultiply-gpubiginteger-gpubiginteger) | Computes multiplication using the Number Theoretic Transform. |

## NttMultiply(GpuBigInteger, GpuBigInteger)

Builds a lazy-evaluated expression tree node for NTT-based multiplication. 

```csharp
public static GpuBigIntExpr NttMultiply(GpuBigInteger left, GpuBigInteger right);
```

#### Parameters

`left` _GpuBigInteger_<br>
The first value to multiply.

`right` _GpuBigInteger_<br>
The second value to multiply.

#### Returns

_GpuBigIntExpr_<br>
An expression tree node that, when evaluated, produces the product using an $O(N \log N)$ convolution.

## Examples

### Massive Number Convolution ($O(N \log N)$)

When searching for massive prime numbers or calculating millions of digits of $\pi$, you are often required to multiply two numbers that are megabytes in size.

```csharp
// Execution Context: Host (CPU)

// Allocate a context capable of handling 2-Megabyte numbers
using var context = new GpuContext(maxWords: 524288); // ~16.7 million bits!

// Assume these are massive arrays loaded from disk or previous stages
byte[] massiveDataA = LoadMegabyteArray();
byte[] massiveDataB = LoadMegabyteArray();

using var gpuDataA = GpuUIntBigInteger.FromByteArray(context, massiveDataA);
using var gpuDataB = GpuUIntBigInteger.FromByteArray(context, massiveDataB);

var a = new GpuBigInteger(gpuDataA);
var b = new GpuBigInteger(gpuDataB);

// Build the NTT AST Node
// 1. Forward Transform (A -> A')
// 2. Forward Transform (B -> B')
// 3. Pointwise Multiplication (A' * B' = C')
// 4. Inverse Transform (C' -> C)
var nttExpr = GpuBigInteger.NttMultiply(a, b);

// Evaluate: The orchestrator dispatches the complex sequence of 
// Cooley-Tukey butterfly kernels across the GPU seamlessly.
using var result = nttExpr.Node.Evaluate(context, null);

Console.WriteLine($"Result Size: {result.Value.NumWords} words.");
```

## Remarks

The standard `*` operator automatically falls back to NTT if the operand sizes cross a specific heuristic threshold (e.g., > 10,000 words). However, you can use `NttMultiply` to force the NTT code path explicitly.

## See also

### Related Algorithms
- [ModPow](ModPow.md)
- [DivRem](DivRem.md)
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
- [Precision and Performance Guide](../../../../04_guides/05_precision_and_performance_guide.md)
