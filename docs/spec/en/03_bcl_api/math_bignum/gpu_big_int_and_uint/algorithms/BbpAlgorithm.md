---
title: "BbpAlgorithm Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Implements the Bailey-Borwein-Plouffe (BBP) spigot algorithm for direct extraction of arbitrary hexadecimal Pi digits."
---

## BbpAlgorithm Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Implements the Bailey-Borwein-Plouffe (BBP) spigot algorithm for direct extraction of arbitrary hexadecimal $\pi$ (Pi) digits without computing preceding digits.

```csharp
public static class BbpAlgorithm
```

## Methods

| Name | Description |
| --- | --- |
| [ExtractHexDigits(Int32, Int32)](#extracthexdigitsint32-int32) | Computes the sequence of hexadecimal digits of Pi immediately following the specified 0-indexed fractional offset. |

## ExtractHexDigits(Int32, Int32)

Computes the sequence of hexadecimal digits of $\pi$ immediately following the specified position (`id`). For example, `id = 0` returns digits starting at the 1st hexadecimal fractional digit (`"243F6A8885A308D3..."`). Automatically chunks requests exceeding 8 digits to maintain double-precision accuracy across arbitrary lengths.

```csharp
public static string ExtractHexDigits(int id, int digitCount = 16);
```

#### Parameters

`id` _Int32_<br>
The 0-indexed fractional hexadecimal digit position offset.

`digitCount` _Int32_<br>
Total number of hexadecimal digits to extract. Defaults to `16`. If less than or equal to zero, returns `string.Empty`.

#### Returns

_String_<br>
A string containing the extracted hexadecimal digits in uppercase.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`id` is negative.

## Remarks

`BbpAlgorithm` evaluates the BBP formula:

$$\pi = \sum_{k=0}^{\infty} \frac{1}{16^k} \left( \frac{4}{8k+1} - \frac{2}{8k+4} - \frac{1}{8k+5} - \frac{1}{8k+6} \right)$$

using modular binary exponentiation ($16^{d-k} \bmod (8k+m)$) in chunks of up to 8 hexadecimal digits per pass. When `id >= 10000`, the series summation automatically parallelizes across CPU threads via `Parallel.For`. It is primarily used by `ChudnovskyAlgorithm` and `PiVerifier` to verify the trailing hexadecimal digits of multi-million-digit GPU $\pi$ computations.

## Examples

### Extracting Hexadecimal Digits of Pi

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Execution Context: Host (CPU)
string hexPi = BbpAlgorithm.ExtractHexDigits(id: 0, digitCount: 8);
Console.WriteLine($"Pi hex [0..8): {hexPi}");
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [ChudnovskyAlgorithm Class](ChudnovskyAlgorithm.md)
- [Hypergeometric Series (GpuHypergeometricPqt)](Hypergeometric.md)
