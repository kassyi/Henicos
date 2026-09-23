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

## Mechanism

The NTT multiplication is an **internal algorithm**. There is no public `NttMultiply` or `Mul` method. Instead, the standard `*` operator (or expression tree multiplication) automatically routes to the NTT implementation when the size of the operands crosses a performance threshold.

```csharp
// Internally dispatched when evaluating:
var expr = gpuA * gpuB; 
```

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
using var result = new GpuUIntBigInteger(context, a.Value.NumWords + b.Value.NumWords);
GpuBigIntMath.Mul(a.Value, b.Value, result);

Console.WriteLine($"Result Size: {result.Value.NumWords} words.");
```

## Remarks

The standard `*` operator automatically falls back to NTT if the operand sizes cross a specific heuristic threshold (e.g., > 10,000 words). However, you can use `NttMultiply` to force the NTT code path explicitly.

## See also



### Language Specification

- [1. Language Specification](../../../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../../../01_language/04_execution_model.md)

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuBigInteger and GpuUIntBigInteger Operators (G#)](../Operators.md)

