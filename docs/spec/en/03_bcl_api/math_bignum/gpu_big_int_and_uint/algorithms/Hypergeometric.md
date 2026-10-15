---
title: "HypergeometricTerms Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Reference for Hypergeometric Series (Chudnovsky algorithm) binary splitting terms."
---

## HypergeometricTerms Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

The `HypergeometricTerms` struct (formerly `GpuHypergeometricPqt`) provides specialized primitives for evaluating hypergeometric series using **Binary Splitting** on the GPU. Its primary use case in computational mathematics is the [ChudnovskyAlgorithm](ChudnovskyAlgorithm.md), which is used for calculating millions of digits of $\pi$ (Pi).

```csharp
public readonly struct HypergeometricTerms(
    [TakesOwnership] GpuBigInt p,
    [TakesOwnership] GpuBigUInt q,
    [TakesOwnership] GpuBigInt t) : IDisposable
```

## Binary Splitting

The algorithm relies on **Binary Splitting**, which recursively computes three terms: $P$, $Q$, and $T$.

- $P$: Product of numerators ($P = P_l \cdot P_r$).
- $Q$: Product of denominators ($Q = Q_l \cdot Q_r$).
- $T$: Partial sum ($T = T_l \cdot Q_r + P_l \cdot T_r$).

Because binary splitting is a reduction tree, subtree states can be merged directly in GPU VRAM via `HypergeometricTerms.Merge(workspace, left, right)`.

## Properties

| Name | Description |
| --- | --- |
| `P` | Gets the `P` term (`GpuBigInt`). |
| `Q` | Gets the `Q` term (`GpuBigUInt`). |
| `T` | Gets the `T` term (`GpuBigInt`). |

## Methods

| Name | Description |
| --- | --- |
| `Dispose()` | Releases the GPU global device memory resources allocated for `P`, `Q`, and `T`. |
| `Merge(GpuWorkspace, HypergeometricTerms, HypergeometricTerms)` | Merges two adjacent `HypergeometricTerms` states on the GPU. |

## Examples

### Merging Hypergeometric Binary Splitting States on the GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

var pLeft = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
var qLeft = GpuBigUInt.FromBigInteger(workspace, 1, 4);
var tLeft = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
using var left = new HypergeometricTerms(pLeft, qLeft, tLeft);

var pRight = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
var qRight = GpuBigUInt.FromBigInteger(workspace, 1, 4);
var tRight = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
using var right = new HypergeometricTerms(pRight, qRight, tRight);

using var merged = HypergeometricTerms.Merge(workspace, left, right);

Console.WriteLine($"pNew = {merged.P.Value.ToBigInteger(merged.P.IsNegative)}");
Console.WriteLine($"qNew = {merged.Q.ToBigInteger(false)}");
Console.WriteLine($"tNew = {merged.T.Value.ToBigInteger(merged.T.IsNegative)}");
```

## Remarks

The `HypergeometricTerms` structure holds the three GPU buffers required for a single binary splitting frame on the GPU memory heap. See [GpuHypergeometricPqt.md](../GpuHypergeometricPqt.md) for the complete API signature reference.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [ChudnovskyAlgorithm Class](ChudnovskyAlgorithm.md)
- [Arbitrary-Precision Floats (GpuBigFloat)](../../gpu_big_float/00_index.md)
