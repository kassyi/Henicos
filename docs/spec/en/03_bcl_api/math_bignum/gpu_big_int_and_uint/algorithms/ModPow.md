---
title: "GpuBigIntDivRem.ModPow Method (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Reference for multi-precision Modular Exponentiation (RSA) on the GPU."
---

## GpuBigIntDivRem.ModPow Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Computes the modular exponentiation $a^b \pmod m$ of extremely large integers on the GPU.

> [!IMPORTANT]
> **Poly-Algorithm Dispatcher & Zero-Sync Fallback**
> ModPow uses a poly-algorithm architecture governed by `GpuWorkspace.PolyAlgorithmThreshold` (default: 1024 bits).
> - **MegaKernel (NTT-Barrett)**: Eagerly evaluated within GPU hardware registers. Provides extreme speedup for operands $\le 1024$ bits.
> - **Zero-Sync Controller**: For giant-scale operands $> 1024$ bits where MegaKernels suffer catastrophic register spilling, ModPow safely falls back to a Zero-Sync model. This mode dispatches thousands of operations without CPU `cuStreamSynchronize` overhead, allowing `ModPow` to be safely captured entirely within CUDA Graphs.

## Overloads

| Name | Description |
| --- | --- |
| [ModPow(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modpowgpubiguint-gpubiguint-gpubiguint-gpubiguint) | Computes the modular exponentiation of a number. |

## ModPow(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes modular exponentiation ($(\text{baseVal}^{\text{exp}} \bmod \text{mod})$) on the GPU using binary exponentiation (square-and-multiply).

```csharp
public static void ModPow(GpuBigUInt baseVal, GpuBigUInt exp, GpuBigUInt mod, GpuBigUInt resultBuffer);
```

#### Parameters

`baseVal` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The base value.

`exp` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The exponent value.

`mod` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The modulus.

`resultBuffer` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the modular power.

#### Exceptions

`ArgumentException`<br>
Thrown when `baseVal`, `mod`, and `resultBuffer` have mismatched word lengths.

`DivideByZeroException`<br>
Thrown when `mod` is zero.

## Examples

### RSA Encryption / Decryption Orchestration

A concrete example demonstrating how `ModPow` is used to orchestrate RSA decryption ($Message = Ciphertext^d \pmod n$) across the GPU. This is highly effective when batch-processing thousands of payloads simultaneously.

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Allocate a workspace for modular exponentiation
using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

// 1. Prepare RSA Parameters (Modulus 'n', Private Exponent 'd', Ciphertext 'c')
using var gpuModulus = GpuBigUInt.FromBigInteger(workspace, new BigInteger(3233), minWords: 4);
using var gpuPrivateKey = GpuBigUInt.FromBigInteger(workspace, new BigInteger(17), minWords: 4);
using var gpuCiphertext = GpuBigUInt.FromBigInteger(workspace, new BigInteger(65), minWords: 4);

// 2. Eagerly evaluate: c^d mod n (65^17 mod 3233 = 2790)
using var result = new GpuBigUInt(workspace, 4);
GpuBigIntDivRem.ModPow(gpuCiphertext, gpuPrivateKey, gpuModulus, result);

// 3. Download the result back to the CPU
Console.WriteLine($"Computed payload: {result.ToBigInteger(false)}");
```

## Remarks

This orchestration leverages Montgomery/Barrett reduction to prevent massive allocation of intermediate memory during exponentiation.

## See also

### BCL API > Math Bignum

- [GpuBigIntDivRem Class (G#)](../GpuBigIntDivRem.md)
- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [FermatAlgorithm Class (G#)](FermatAlgorithm.md)
