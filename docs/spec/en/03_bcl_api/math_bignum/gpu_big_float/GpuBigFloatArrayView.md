---
title: "GpuBigFloatArrayView Struct (G#)"
site: "G# Documentation"
published: "2026-10-28T00:00:00.000Z"
language: "en"
description: "A borrowed, stack-only view of a range of elements of a GpuBigFloatArray."
---

## GpuBigFloatArrayView Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A borrowed view of a contiguous range of elements of a `GpuBigFloatArray`: views of the significands, the exponents and the sign words. `GpuBigFloatArray.Slice` returns it.

```csharp
public readonly ref struct GpuBigFloatArrayView
```

## Remarks

The rules of [`GpuBigUIntView`](../gpu_big_int_and_uint/GpuBigUIntView.md) apply: the view lives only inside the method that obtained it, has no `Dispose`, and must not be used after the array is disposed. A `GpuBigFloatArray` converts to a view implicitly.

## Properties

| Name | Description |
| --- | --- |
| `Significands` | A `GpuBigUIntArrayView` of the significands. |
| `Exponents` | A `GpuBigUIntArrayView` of the exponents (one word per element, two's complement `int` bits). |
| `Signs` | A `GpuBigUIntArrayView` of the sign words (0 = positive, 1 = negative). |
| `Length`, `WordsPerElement`, `Workspace`, `IsEmpty` | As on `GpuBigUIntArrayView`. |

## Methods

| Name | Description |
| --- | --- |
| `GetElement(int index)` | A [`GpuBigFloatView`](GpuBigFloatView.md) of one element; reads its exponent and sign back from the device. |
| `Slice(int elementOffset, int elementCount)` | A view of a range of the elements. |
