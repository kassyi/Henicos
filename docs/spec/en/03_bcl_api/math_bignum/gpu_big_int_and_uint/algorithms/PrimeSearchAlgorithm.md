---
title: "PrimeSearchAlgorithm Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides high-performance prime generation utilizing CPU trial factoring and Fermat PRP testing."
---

## PrimeSearchAlgorithm Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Generates probable primes (PRP) on the CPU with trial division and the Miller-Rabin test (formerly `GpuPrimeGenerator`). Nothing runs on the GPU, for any bit length.

```csharp
public static class PrimeSearchAlgorithm
```

## Methods

| Name | Description |
| --- | --- |
| [Generate(Int32)](#generateint32) | Generates a strictly positive probable prime (PRP) of the specified bit length. |

## Generate(Int32)

Generates a strictly positive probable prime (PRP) of the specified bit length. Uses cryptographic random candidate generation (`RandomNumberGenerator.Fill`) with the most-significant bit and least-significant bit set, CPU trial division against small primes, and 10 rounds of the Miller-Rabin test with `BigInteger`.

```csharp
public static BigInteger Generate(int bitLength);
```

#### Parameters

`bitLength` _Int32_<br>
The required bit length of the prime (must be at least `2`).

#### Returns

_BigInteger_<br>
A generated probable prime of the specified bit length.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`bitLength` is less than `2`.

## Examples

### Generating a Probable Prime

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Execution Context: Host (CPU)
BigInteger prime = PrimeSearchAlgorithm.Generate(bitLength: 64);
Console.WriteLine($"Generated 64-bit prime > 0: {prime > 0}, BitLength: {prime.GetBitLength()}");
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [FermatAlgorithm Class](FermatAlgorithm.md)
