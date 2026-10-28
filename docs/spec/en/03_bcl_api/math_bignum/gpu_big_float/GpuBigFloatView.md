---
title: "GpuBigFloatView Struct (G#)"
site: "G# Documentation"
published: "2026-10-28T00:00:00.000Z"
language: "en"
description: "A borrowed, stack-only view of an arbitrary-precision floating-point number in GPU device memory."
---

## GpuBigFloatView Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A borrowed view of a signed arbitrary-precision floating-point number: a view of the significand, the exponent and the sign. Its value is `(-1)^IsNegative * Significand * 2^Exponent`. `GpuBigFloatArray.GetElement` returns it, and `GpuBigFloatArray.SetElement` takes it.

```csharp
public readonly ref struct GpuBigFloatView
```

## Remarks

The rules of [`GpuBigUIntView`](../gpu_big_int_and_uint/GpuBigUIntView.md) apply: the view lives only inside the method that obtained it, has no `Dispose`, and must not be used after its owner is disposed. A [`GpuBigFloat`](GpuBigFloat.md) converts to a view implicitly; `ToOwned()` copies the value into a `GpuBigFloat` that the caller owns.

## Properties

| Name | Description |
| --- | --- |
| `Significand` | A `GpuBigUIntView` of the significand. |
| `Exponent` | The binary exponent (read back when the element view was created). |
| `IsNegative` | Whether the value is negative (read back when the element view was created). |
| `Workspace` | The `GpuWorkspace` of the viewed buffer. |
| `IsEmpty` | `true` for `default(GpuBigFloatView)`. |

## Methods

| Name | Description |
| --- | --- |
| `ToOwned()` | Copies the value into a new `GpuBigFloat`. |
| `IsZero()` | Whether the significand is zero. |
| `CompareTo(scoped GpuBigFloatView other)` | Compares the values on the GPU. |
