---
title: "GpuBigIntArrayView Struct (G#)"
site: "G# Documentation"
published: "2026-10-28T00:00:00.000Z"
language: "en"
description: "A borrowed, stack-only view of a range of elements of a GpuBigIntArray."
---

## GpuBigIntArrayView Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A borrowed view of a contiguous range of elements of a [`GpuBigIntArray`](GpuBigIntArray.md): views of the values and of the sign words. `GpuBigIntArray.Slice` returns it.

```csharp
public readonly ref struct GpuBigIntArrayView
```

## Remarks

The rules of [`GpuBigUIntView`](GpuBigUIntView.md) apply: the view lives only inside the method that obtained it, has no `Dispose`, and must not be used after the array is disposed. A `GpuBigIntArray` converts to a view implicitly.

## Properties

| Name | Description |
| --- | --- |
| `Values` | A [`GpuBigUIntArrayView`](GpuBigUIntArrayView.md) of the absolute values. |
| `Signs` | A `GpuBigUIntArrayView` of the sign words (0 = positive, 1 = negative). |
| `Length`, `WordsPerElement`, `Workspace`, `IsEmpty` | As on `GpuBigUIntArrayView`. |

## Methods

| Name | Description |
| --- | --- |
| `GetElement(int index)` | A [`GpuBigIntView`](GpuBigIntView.md) of one element; reads its sign word back from the device. |
| `Slice(int elementOffset, int elementCount)` | A view of a range of the elements. |
