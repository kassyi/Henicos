---
title: "GpuBigInt.NttMultiply Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for the Number Theoretic Transform (NTT) for O(N log N) multiplication."
---

## GpuBigInt.NttMultiply Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

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

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)

using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt;

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

The standard `*` operator automatically falls back to NTT if the operand sizes cross a specific heuristic threshold (e.g., > 10,000 words). However, you can use `NttMultiply` to force the NTT code path explicitly.

## NttCpuReference Class

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt.Algorithms`

A pure CPU reference implementation combining the 3-Prime NTT (Number Theoretic Transform) and Garner's CRT algorithm for large integer multiplication. It is used for host-side validation against the GPU results.

```csharp
public static class NttCpuReference
{
    public const uint P1 = 2130706433;
    public const uint P2 = 1224736769;
    public const uint P3 = 754974721;
    public const uint G1 = 3;
    public const uint G2 = 3;
    public const uint G3 = 11;

    public static uint ModPow(uint baseValue, uint exponent, uint modulus);
    public static uint ModInverse(uint value, uint modulus);
    public static uint ModAdd(uint operandA, uint operandB, uint modulus);
    public static uint ModSub(uint operandA, uint operandB, uint modulus);
    public static uint ModMul(uint operandA, uint operandB, uint modulus);
    public static void Ntt(uint[] values, uint modulus, uint primitiveRoot, bool isInverse);
    public static uint[] Multiply(uint[] operandA, uint[] operandB);
}
```

## See also

### Language Specification

- [1. Language Specification](../../../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../../../01_language/04_execution_model.md)

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuBigInt and GpuBigUInt Operators (G#)](../Operators.md)
