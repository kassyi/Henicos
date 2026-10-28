---
title: "GpuBigIntView Struct (G#)"
site: "G# Documentation"
published: "2026-10-28T00:00:00.000Z"
language: "en"
description: "A borrowed, stack-only view of a signed arbitrary-precision integer in GPU device memory."
---

## GpuBigIntView Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A borrowed view of a signed arbitrary-precision integer: a [`GpuBigUIntView`](GpuBigUIntView.md) of the magnitude and the sign. `GpuBigIntArray.GetElement` returns it.

```csharp
public readonly ref struct GpuBigIntView
```

## Remarks

The rules of [`GpuBigUIntView`](GpuBigUIntView.md) apply: the view lives only inside the method that obtained it, has no `Dispose`, and must not be used after its owner is disposed. A [`GpuBigInt`](GpuBigInt.md) converts to a view implicitly; `ToOwned()` copies the value into a `GpuBigInt` that the caller owns.

## Properties

| Name | Description |
| --- | --- |
| `Value` | The view of the absolute value. |
| `IsNegative` | Whether the value is negative (read back when the element view was created). |
| `Workspace` | The `GpuWorkspace` of the viewed buffer. |
| `IsEmpty` | `true` for `default(GpuBigIntView)`. |

## Methods

| Name | Description |
| --- | --- |
| `ToOwned()` | Copies the value into a new `GpuBigInt`. |
| `IsZero()`, `GetBitLength()` | As on `GpuBigInt`. |
| `CompareTo(scoped GpuBigIntView other)` | Compares the values on the GPU; `+0` equals `-0`. |
| `ToBigInteger()` | Reads the signed value back to the host. |
