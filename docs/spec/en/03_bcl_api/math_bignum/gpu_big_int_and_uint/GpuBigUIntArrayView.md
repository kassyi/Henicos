---
title: "GpuBigUIntArrayView Struct (G#)"
site: "G# Documentation"
published: "2026-10-28T00:00:00.000Z"
language: "en"
description: "A borrowed, stack-only view of a range of elements of a GpuBigUIntArray."
---

## GpuBigUIntArrayView Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A borrowed view of a contiguous range of elements of a [`GpuBigUIntArray`](GpuBigUIntArray.md), returned by `GpuBigUIntArray.Slice`.

```csharp
public readonly ref struct GpuBigUIntArrayView
```

## Remarks

The rules of [`GpuBigUIntView`](GpuBigUIntView.md) apply: the view lives only inside the method that obtained it, has no `Dispose`, and must not be used after the array is disposed (its `DevicePointer` then throws `ObjectDisposedException`). A `GpuBigUIntArray` converts to a view implicitly.

## Properties

| Name | Description |
| --- | --- |
| `Workspace` | The `GpuWorkspace` of the viewed buffer. |
| `Length` | The number of elements. |
| `WordsPerElement` | The number of 32-bit words per element. |
| `TotalWords` | `Length * WordsPerElement`. |
| `DevicePointer` | The device address of the first element. |
| `IsEmpty` | `true` for `default(GpuBigUIntArrayView)`. |

## Methods

| Name | Description |
| --- | --- |
| `GetElement(int index)` | A [`GpuBigUIntView`](GpuBigUIntView.md) of one element. |
| `Slice(int elementOffset, int elementCount)` | A view of a range of the elements. |
