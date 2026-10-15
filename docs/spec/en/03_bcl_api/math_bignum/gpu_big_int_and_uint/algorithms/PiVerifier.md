---
title: "PiVerifier Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides verification functionality for computed Pi digits against the BBP spigot algorithm."
---

## PiVerifier Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms.Diagnostics`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides verification functionality for computed $\pi$ (Pi) digits by comparing trailing hexadecimal digits against [BbpAlgorithm](BbpAlgorithm.md).

```csharp
public static class PiVerifier
```

## Methods

| Name | Description |
| --- | --- |
| [VerifyWithBbp(String, Int32)](#verifywithbbpstring-int32) | Verifies the trailing hexadecimal digits of the computed Pi against the BBP algorithm. |

## VerifyWithBbp(String, Int32)

Verifies the trailing hexadecimal digits of the computed $\pi$ against the BBP algorithm.

```csharp
public static bool VerifyWithBbp(string computedHexTail, int offset);
```

#### Parameters

`computedHexTail` _String_<br>
The trailing hexadecimal digits computed from the GPU result.

`offset` _Int32_<br>
The 0-based fractional hexadecimal digit index of the first digit in `computedHexTail`.

#### Returns

_Boolean_<br>
`true` if `computedHexTail` is non-empty and matches the BBP-extracted digits (case-insensitive); otherwise, `false`.

## Examples

### Verifying Computed Hexadecimal Pi Digits

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms.Diagnostics;

// Execution Context: Host (CPU)
bool verified = PiVerifier.VerifyWithBbp("243F6A88", offset: 0);
Console.WriteLine($"BBP Verified: {verified}");
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [BbpAlgorithm Class](BbpAlgorithm.md)
- [ChudnovskyAlgorithm Class](ChudnovskyAlgorithm.md)
