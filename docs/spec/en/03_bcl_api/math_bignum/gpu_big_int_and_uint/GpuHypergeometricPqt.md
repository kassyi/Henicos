---
title: "HypergeometricTerms Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents the intermediate tuple state (P, Q, T) of a hypergeometric series evaluated using Binary Splitting on the GPU."
---

## HypergeometricTerms Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents the intermediate tuple state $(P, Q, T)$ of a hypergeometric series evaluated using Binary Splitting on the GPU (formerly `GpuHypergeometricPqt`).

```csharp
public readonly struct HypergeometricTerms : IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [HypergeometricTerms(GpuBigInt, GpuBigUInt, GpuBigInt)](#hypergeometrictermsgpubigint-gpubiguint-gpubigint) | Initializes a new instance of the `HypergeometricTerms` struct. |

## HypergeometricTerms(GpuBigInt, GpuBigUInt, GpuBigInt)

Initializes a new instance of the `HypergeometricTerms` struct.

```csharp
public HypergeometricTerms(
    [TakesOwnership] GpuBigInt p,
    [TakesOwnership] GpuBigUInt q,
    [TakesOwnership] GpuBigInt t);
```

#### Parameters

`p` _GpuBigInt_<br>
The cumulative $P$ product component. Takes ownership of the buffer.

`q` _GpuBigUInt_<br>
The cumulative $Q$ product component. Takes ownership of the buffer.

`t` _GpuBigInt_<br>
The cumulative $T$ partial sum component. Takes ownership of the buffer.

## Properties

| Name | Description |
| --- | --- |
| [P](#p) | Gets the cumulative product term $P(a, b) = \prod_{k=a}^{b-1} p(k)$. |
| [Q](#q) | Gets the cumulative product term $Q(a, b) = \prod_{k=a}^{b-1} q(k)$. |
| [T](#t) | Gets the cumulative summation term $T(a, b)$. |

## P

Gets the cumulative product term $P(a, b) = \prod_{k=a}^{b-1} p(k)$.

```csharp
public GpuBigInt P { get; }
```

## Q

Gets the cumulative product term $Q(a, b) = \prod_{k=a}^{b-1} q(k)$.

```csharp
public GpuBigUInt Q { get; }
```

## T

Gets the cumulative summation term $T(a, b)$.

```csharp
public GpuBigInt T { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Releases the GPU global device memory resources allocated for `P`, `Q`, and `T`. |
| [Merge(GpuWorkspace, HypergeometricTerms, HypergeometricTerms)](#mergegpuworkspace-hypergeometricterms-hypergeometricterms) | Merges two adjacent `HypergeometricTerms` states on the GPU. |

## Dispose()

Releases the GPU global device memory resources allocated for `P`, `Q`, and `T`.

```csharp
public void Dispose();
```

## Merge(GpuWorkspace, HypergeometricTerms, HypergeometricTerms)

Merges two adjacent `HypergeometricTerms` states $(P_l, Q_l, T_l)$ and $(P_r, Q_r, T_r)$ on the GPU.

```csharp
public static HypergeometricTerms Merge(
    GpuWorkspace workspace,
    HypergeometricTerms left,
    HypergeometricTerms right);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The parent GPU execution context managing memory and kernel execution.

`left` _HypergeometricTerms_<br>
The left child binary splitting state $(P_l, Q_l, T_l)$.

`right` _HypergeometricTerms_<br>
The right child binary splitting state $(P_r, Q_r, T_r)$.

#### Returns

_HypergeometricTerms_<br>
A newly merged `HypergeometricTerms` state where $P = P_l P_r$, $Q = Q_l Q_r$, and $T = T_l Q_r + P_l T_r$.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `workspace` is `null`.

## Examples

### Binary Splitting State Merging

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

// Initialize two adjacent states for binary splitting (P=1, Q=1, T=1)
var pLeft = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
var qLeft = GpuBigUInt.FromBigInteger(workspace, 1, 4);
var tLeft = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
using var stateLeft = new HypergeometricTerms(pLeft, qLeft, tLeft);

var pRight = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
var qRight = GpuBigUInt.FromBigInteger(workspace, 1, 4);
var tRight = new GpuBigInt(GpuBigUInt.FromBigInteger(workspace, 1, 4), false);
using var stateRight = new HypergeometricTerms(pRight, qRight, tRight);

// Merge the two states: P = Pl * Pr, Q = Ql * Qr, T = Tl * Qr + Pl * Tr
using var merged = HypergeometricTerms.Merge(workspace, stateLeft, stateRight);

Console.WriteLine($"Merged P = {merged.P.Value.ToBigInteger(merged.P.IsNegative)}");
Console.WriteLine($"Merged Q = {merged.Q.ToBigInteger(false)}");
Console.WriteLine($"Merged T = {merged.T.Value.ToBigInteger(merged.T.IsNegative)}");
```

## Remarks

Defined according to the Binary Splitting algorithm for evaluating series of the form $\sum \frac{a(k) \cdot p(0) \cdots p(k)}{b(k) \cdot q(0) \cdots q(k)}$:
- `P`: $P(a, b) = \prod_{k=a}^{b-1} p(k)$
- `Q`: $Q(a, b) = \prod_{k=a}^{b-1} q(k)$
- `T`: $T(a, b) = \sum_{k=a}^{b-1} a(k) p(a)\cdots p(k) q(k+1)\cdots q(b-1)$

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [ChudnovskyAlgorithm Class](algorithms/ChudnovskyAlgorithm.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
