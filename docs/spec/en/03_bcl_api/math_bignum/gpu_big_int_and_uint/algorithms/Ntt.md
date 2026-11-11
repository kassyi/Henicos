---
title: "NTT Multiplication (GpuBigIntMath.MultiplyNtt) (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for the Number Theoretic Transform (NTT) for O(N log N) multiplication."
---

## NTT Multiplication (GpuBigIntMath.MultiplyNtt)

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

When multiplying two numbers, the standard "schoolbook" multiplication algorithm operates in $O(N^2)$ time. This is acceptable for numbers up to a few thousand bits. However, when multiplying numbers with **millions of bits**, $O(N^2)$ becomes impossibly slow.

To solve this, GSharp provides an ultra-optimized **Number Theoretic Transform (NTT)** implementation. NTT is a discrete Fourier transform over a finite field. It reduces the time complexity of multiplication from $O(N^2)$ to $O(N \log N)$ by converting the multiplication into a convolution in the frequency domain.

## Mechanism

The `*` operator (or expression tree multiplication) and [`GpuBigIntMath.Mul`](../GpuBigIntMath.md) choose the multiplication algorithm from the size of the operands and route large products to the NTT implementation. [`GpuBigIntMath.MultiplyNtt`](../GpuBigIntMath.md) runs the NTT path whatever the size:

```csharp
public static void Mul(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
public static void MultiplyNtt(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
```

Both throw `ArgumentException` when `resultBuffer` has fewer than `leftOperand.NumWords + rightOperand.NumWords` words.

## Examples

### Massive Number Convolution ($O(N \log N)$)

When searching for massive prime numbers or calculating millions of digits of $\pi$, you are often required to multiply two numbers that are megabytes in size.

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)

using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Allocate a workspace capable of handling 2-Megabyte numbers
using var workspace = new GpuWorkspace(maxWords: 524288); // ~16.7 million bits!

// Assume these are massive arrays loaded from disk or previous stages
byte[] massiveDataA = new byte[1024]; // dummy implementation for test
byte[] massiveDataB = new byte[1024]; // dummy implementation for test

using var gpuDataA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(massiveDataA));
using var gpuDataB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(massiveDataB));

var a = new GpuBigInt(gpuDataA, false);
var b = new GpuBigInt(gpuDataB, false);

// Using the standard * operator builds an AST Node
var expr = a * b;

// Evaluate the AST node to perform the multiplication natively on the GPU
// (Internally routes to the NTT implementation for massive numbers)
using var result = expr.Node.Evaluate(workspace, null);

Console.WriteLine($"Result Size: {result.Value.NumWords} words.");
```

## Remarks

The standard `*` operator switches to NTT when the operands are large enough for it to be faster. Use `GpuBigIntMath.MultiplyNtt` to force the NTT code path explicitly:

```csharp
using var product = new GpuBigUInt(workspace, gpuDataA.NumWords + gpuDataB.NumWords);
GpuBigIntMath.MultiplyNtt(gpuDataA, gpuDataB, product);
```

## See also

### Language Specification

- [1. Language Specification](../../../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../../../01_language/04_execution_model.md)

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuBigInt and GpuBigUInt Operators (G#)](../Operators.md)
