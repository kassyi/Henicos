---
title: "GpuHypergeometricPqt Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents the intermediate state (P, Q, T) of a hypergeometric series evaluated using Binary Splitting on the GPU."
---

## GpuHypergeometricPqt Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents the intermediate state (P, Q, T) of a hypergeometric series evaluated using Binary Splitting on the GPU.

```csharp
public readonly struct GpuHypergeometricPqt : IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuHypergeometricPqt(GpuBigInteger, GpuUIntBigInteger, GpuBigInteger)](#gpuhypergeometricpqtgpubiginteger-gpuuintbiginteger-gpubiginteger) | Initializes a new instance of the `GpuHypergeometricPqt` struct. |

## GpuHypergeometricPqt(GpuBigInteger, GpuUIntBigInteger, GpuBigInteger)

Initializes a new instance of the `GpuHypergeometricPqt` struct.

```csharp
public GpuHypergeometricPqt([TakesOwnership] GpuBigInteger p, [TakesOwnership] GpuUIntBigInteger q, [TakesOwnership] GpuBigInteger t);
```

#### Parameters

`p` _GpuBigInteger_<br>
The `P` component of the state.

`q` _GpuUIntBigInteger_<br>
The `Q` component of the state.

`t` _GpuBigInteger_<br>
The `T` component of the state.

## Properties

| Name | Description |
| --- | --- |
| [P](#p) | Gets the P component: `P(a, b) = \prod p(k)` |
| [Q](#q) | Gets the Q component: `Q(a, b) = \prod q(k)` |
| [T](#t) | Gets the T component: `T(a, b) = \sum ...` |

## P

Gets the P component: `P(a, b) = \prod p(k)`

```csharp
public GpuBigInteger P { get; }
```

## Q

Gets the Q component: `Q(a, b) = \prod q(k)`

```csharp
public GpuUIntBigInteger Q { get; }
```

## T

Gets the T component: `T(a, b) = \sum ...`

```csharp
public GpuBigInteger T { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Disposes the underlying GPU buffers for the P, Q, and T states. |
| [Merge(GpuWorkspace, GpuHypergeometricPqt, GpuHypergeometricPqt)](#mergegpuworkspace-gpuhypergeometricpqt-gpuhypergeometricpqt) | Merges two adjacent `GpuHypergeometricPqt` states on the GPU. |

## Dispose()

Disposes the underlying GPU buffers for the P, Q, and T states.

```csharp
public void Dispose();
```

## Merge(GpuWorkspace, GpuHypergeometricPqt, GpuHypergeometricPqt)

Merges two adjacent `GpuHypergeometricPqt` states on the GPU.

```csharp
public static GpuHypergeometricPqt Merge(GpuWorkspace workspace, GpuHypergeometricPqt left, GpuHypergeometricPqt right);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace for temporary allocations.

`left` _GpuHypergeometricPqt_<br>
The left state to merge.

`right` _GpuHypergeometricPqt_<br>
The right state to merge.

#### Returns

_GpuHypergeometricPqt_<br>
A newly merged `GpuHypergeometricPqt` containing the combined P, Q, and T components.

## Examples

### Binary Splitting State Merging

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU) or G# Kernel (GPU)

using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

// Initialize two adjacent states for binary splitting (P=1, Q=1, T=1)
var pLeft = new GpuBigInteger(GpuUIntBigInteger.FromBigInteger(workspace, 1, 4), false);
var qLeft = GpuUIntBigInteger.FromBigInteger(workspace, 1, 4);
var tLeft = new GpuBigInteger(GpuUIntBigInteger.FromBigInteger(workspace, 1, 4), false);
using var stateLeft = new GpuHypergeometricPqt(pLeft, qLeft, tLeft);

var pRight = new GpuBigInteger(GpuUIntBigInteger.FromBigInteger(workspace, 1, 4), false);
var qRight = GpuUIntBigInteger.FromBigInteger(workspace, 1, 4);
var tRight = new GpuBigInteger(GpuUIntBigInteger.FromBigInteger(workspace, 1, 4), false);
using var stateRight = new GpuHypergeometricPqt(pRight, qRight, tRight);

// Merge the two states
// P = Pl * Pr
// Q = Ql * Qr
// T = Tl * Qr + Pl * Tr
using var merged = GpuHypergeometricPqt.Merge(workspace, stateLeft, stateRight);

Console.WriteLine($"Merged P = {merged.P.Value.ToBigInteger(merged.P.IsNegative)}");
Console.WriteLine($"Merged Q = {merged.Q.ToBigInteger(false)}");
Console.WriteLine($"Merged T = {merged.T.Value.ToBigInteger(merged.T.IsNegative)}");
```

## Remarks

Defined according to the Binary Splitting algorithm:
- `P`: `P(a, b) = \prod p(k)`
- `Q`: `Q(a, b) = \prod q(k)`
- `T`: `T(a, b) = \sum ...`

The merge operation is computed as:
- `P = Pl * Pr`
- `Q = Ql * Qr`
- `T = Tl * Qr + Pl * Tr`

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
