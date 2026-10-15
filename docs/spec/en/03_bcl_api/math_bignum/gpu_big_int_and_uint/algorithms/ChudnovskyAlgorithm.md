---
title: "ChudnovskyAlgorithm Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "GPU-accelerated and CPU-fallback Chudnovsky binary splitting algorithm for high-precision Pi computation."
---

## ChudnovskyAlgorithm Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

GPU-accelerated Chudnovsky binary splitting algorithm for ultra-fast high-precision $\pi$ (Pi) computation, utilizing G# `GpuBigUInt`, NTT multiplication kernels, Newton-Raphson division, and divide-and-conquer radix conversion, alongside CPU binary-splitting fallback methods.

```csharp
public partial class ChudnovskyAlgorithm
```

## Nested Types

| Name | Description |
| --- | --- |
| [ChudnovskyAlgorithm.ChudnovskyPqt](#chudnovskypqt-struct) | Represents the intermediate terms `P`, `Q`, and `T` in the CPU binary splitting subtree evaluation. |

## ChudnovskyPqt Struct

Represents the intermediate terms `P`, `Q`, and `T` in the CPU binary splitting algorithm.

```csharp
public struct ChudnovskyPqt
{
    public BigInteger P;
    public BigInteger Q;
    public BigInteger T;
}
```

## Methods

| Name | Description |
| --- | --- |
| [ComputePi(Int32)](#computepiint32) | Computes Pi up to the specified number of decimal digits on the CPU. |
| [ComputePi(Int32, TextWriter, Int32, Boolean)](#computepiint32-textwriter-int32-boolean) | Computes high-precision Pi using the hybrid CPU/GPU Chudnovsky binary splitting pipeline and streams the output. |
| [ComputeSubtree(Int32, Int32)](#computesubtreeint32-int32) | Computes the binary splitting subtree for the range `[a, b)` on the CPU. |

## ComputePi(Int32)

Computes $\pi$ up to the specified number of decimal digits on the CPU using `System.Numerics.BigInteger`.

```csharp
public static string ComputePi(int digits);
```

#### Parameters

`digits` _Int32_<br>
The number of decimal digits to compute.

#### Returns

_String_<br>
A string representation of $\pi$ (unformatted integer digits `314159...`).

## ComputePi(Int32, TextWriter, Int32, Boolean)

Computes high-precision $\pi$ using the Chudnovsky binary splitting algorithm on the GPU and streams the resulting decimal or hexadecimal representation to `outputWriter`.

```csharp
public static void ComputePi(
    int digits,
    TextWriter outputWriter,
    int thresholdTerms = 8192,
    bool asHex = false);
```

#### Parameters

`digits` _Int32_<br>
The number of decimal digits to compute.

`outputWriter` _TextWriter_<br>
The text writer to receive the computed $\pi$ representation.

`thresholdTerms` _Int32_<br>
The leaf subtree threshold (number of hypergeometric terms) at or below which subtrees are evaluated on the CPU via `ComputeSubtree` before uploading and merging on the GPU via `HypergeometricTerms.Merge`. Defaults to `8192`.

`asHex` _Boolean_<br>
If `true`, outputs in hexadecimal format (`3.243F6A88...`) and automatically verifies trailing hexadecimal digits against [BbpAlgorithm](BbpAlgorithm.md); if `false`, outputs in base-10 format (`3.14159265...`) via `GpuRadixConverter`.

#### Exceptions

`ArgumentNullException`<br>
`outputWriter` is `null`.

`ArgumentOutOfRangeException`<br>
`digits` or `thresholdTerms` is less than or equal to zero.

## ComputeSubtree(Int32, Int32)

Computes the subtree of the binary splitting algorithm for the term range `[a, b)` on the CPU.

```csharp
public static ChudnovskyPqt ComputeSubtree(int a, int b);
```

#### Parameters

`a` _Int32_<br>
The inclusive start index of the range.

`b` _Int32_<br>
The exclusive end index of the range.

#### Returns

_ChudnovskyPqt_<br>
The computed `P`, `Q`, and `T` values for the subtree.

## Remarks

The legacy static wrapper `GpuChudnovskyAlgorithm` is marked `[Obsolete("Use ChudnovskyAlgorithm instead.")]` and forwards directly to `ChudnovskyAlgorithm.ComputePi(int, TextWriter, int, bool)`.

## Examples

### Computing Pi via CPU and Hybrid GPU Binary Splitting

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.IO;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Execution Context: Host (CPU)
ChudnovskyAlgorithm.ChudnovskyPqt subtree = ChudnovskyAlgorithm.ComputeSubtree(0, 2);
Console.WriteLine($"Subtree Q: {subtree.Q}");

string cpuPi = ChudnovskyAlgorithm.ComputePi(digits: 20);
Console.WriteLine($"CPU Pi (20 digits): {cpuPi}");

using var sw = new StringWriter();
ChudnovskyAlgorithm.ComputePi(digits: 20, outputWriter: sw, thresholdTerms: 16, asHex: false);
Console.WriteLine($"GPU Pi: {sw}");
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [BbpAlgorithm Class](BbpAlgorithm.md)
- [Hypergeometric Series (GpuHypergeometricPqt)](Hypergeometric.md)
