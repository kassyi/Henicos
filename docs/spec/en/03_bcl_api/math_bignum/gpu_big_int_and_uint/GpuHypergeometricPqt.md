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
| [Merge(GpuContext, GpuHypergeometricPqt, GpuHypergeometricPqt)](#mergegpucontext-gpuhypergeometricpqt-gpuhypergeometricpqt) | Merges two adjacent `GpuHypergeometricPqt` states on the GPU. |

## Dispose()

Disposes the underlying GPU buffers for the P, Q, and T states.

```csharp
public void Dispose();
```

## Merge(GpuContext, GpuHypergeometricPqt, GpuHypergeometricPqt)

Merges two adjacent `GpuHypergeometricPqt` states on the GPU.

```csharp
public static GpuHypergeometricPqt Merge(GpuContext context, GpuHypergeometricPqt left, GpuHypergeometricPqt right);
```

#### Parameters

`context` _GpuContext_<br>
The GPU context for temporary allocations.

`left` _GpuHypergeometricPqt_<br>
The left state to merge.

`right` _GpuHypergeometricPqt_<br>
The right state to merge.

#### Returns

_GpuHypergeometricPqt_<br>
A newly merged `GpuHypergeometricPqt` containing the combined P, Q, and T components.

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

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
