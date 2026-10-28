---
title: "GpuBigUIntView Struct (G#)"
site: "G# Documentation"
published: "2026-10-28T00:00:00.000Z"
language: "en"
description: "A borrowed, stack-only view of an arbitrary-precision unsigned integer in GPU device memory."
---

## GpuBigUIntView Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A borrowed view of an arbitrary-precision unsigned integer in GPU device memory: an element of a [`GpuBigUIntArray`](GpuBigUIntArray.md), a range of words, or a [`GpuBigUInt`](GpuBigUInt.md) passed to an operation.

```csharp
public readonly ref struct GpuBigUIntView
```

## Remarks

`GpuBigUIntView` is to `GpuBigUInt` what `Span<T>` is to an array:

| Owned (frees its buffer) | Borrowed, may be stored (internal) | Borrowed, stack only (public) |
| --- | --- | --- |
| `GpuBigUInt`, `GpuBigUIntArray` (`Dispose` returns the buffer) | a `GpuBigUInt` whose handle does not own the buffer | `GpuBigUIntView`, `GpuBigUIntArrayView` |
| `T[]`, `IMemoryOwner<T>` | `Memory<T>` | `Span<T>` |

- **Stack only.** The view is a `ref struct`. The compiler rejects storing it in a field of a class or a non-ref struct, capturing it in a lambda or local function, boxing it, using it as a generic type argument (without `allows ref struct`) or an array element, wrapping it in `Nullable<T>`, and using it across an `await`. To keep the value, copy it with `ToOwned()`.
- **No `Dispose`.** A view does not own its buffer, so `using var v = array.GetElement(0);` and `array.GetElement(0).Dispose()` do not compile. Disposing the owner frees the memory.
- **Use after free is checked at run time.** C# does not stop code from disposing the owner and then using a view in the same method (it does not for a `Span<T>` over an array returned to a pool either). The view's `DevicePointer` then throws `ObjectDisposedException`, and so does every member that reads device memory.
- **Implicit conversion from the owner.** A `GpuBigUInt` converts to a view implicitly, as `T[]` converts to `Span<T>`. The operations of [`GpuBigIntMath`](GpuBigIntMath.md) and [`GpuBigIntDivRem`](GpuBigIntDivRem.md) take views, so they accept owned values and element views alike. The conversion does not apply to the receiver of an extension method, to generic type inference or to `ref` / `out` arguments.
- **No expression operators.** `a.GetElement(0) + b` does not compile; build expressions from owned values.
- `ToProxy()` returns the `GSharpArrayProxy` (the raw pointer and length for kernel arguments). The conversion is explicit only, so that `object o = view;` does not compile by boxing the proxy; the proxy is a plain struct outside the ownership rules, like the pointer of a pinned `Memory<T>`.

```csharp
using var array = new GpuBigUIntArray(workspace, 4, 8);
using var sum = new GpuBigUInt(workspace, 8);

GpuBigIntMath.Add(array.GetElement(0), array.GetElement(1), sum);   // views and an owned value mix
using var kept = array.GetElement(2).ToOwned();                      // a copy that outlives the array
```

## Properties

| Name | Description |
| --- | --- |
| `Workspace` | The `GpuWorkspace` of the viewed buffer, or `null` for the empty view. |
| `NumWords` | The number of 32-bit words in the view. |
| `DevicePointer` | The device address of the first word. Throws `ObjectDisposedException` after the owner is disposed. |
| `IsEmpty` | `true` for `default(GpuBigUIntView)`, which refers to no buffer. |

## Methods

| Name | Description |
| --- | --- |
| `Slice(int wordOffset, int numWords)` | A view of a range of the words. |
| `ToOwned()` | Copies the words into a new `GpuBigUInt` that the caller owns. |
| `CopyTo(scoped GpuBigUIntView destination)` | Copies the value and clears the destination's words above it; throws `ArgumentException` for a narrower destination. |
| `CompareTo(scoped GpuBigUIntView other)` | Compares the values on the GPU. |
| `IsZero()`, `IsOne()`, `GetBitLength()`, `GetByteCount()`, `GetIsEven()` | As on `GpuBigUInt`. |
| `Clear(...)`, `ClearWord(int)`, `CopyFrom(scoped GpuBigUIntView, ...)` | Write to the viewed words, as on `GpuBigUInt`. |
| `ToHostArray()`, `GetTopWords(int)`, `ToBigInteger(bool)` | Read the value back to the host. |
| `PadTo(int)`, `ModInverse(scoped GpuBigUIntView)`, `DivideByUInt32(uint)` | Return a new owned `GpuBigUInt`. |
| `ToProxy()` | The `GSharpArrayProxy` of the viewed words. |

The asynchronous members of `GpuBigUInt` (`ToHostArrayAsync` and others) are not on the view: a `ref struct` cannot be used by an `async` method. Call them on the owner.

## See also

- [GpuBigUIntArrayView](GpuBigUIntArrayView.md), [GpuBigIntView](GpuBigIntView.md), [GpuBigIntArrayView](GpuBigIntArrayView.md)
- [GpuBigFloatView](../gpu_big_float/GpuBigFloatView.md), [GpuBigFloatArrayView](../gpu_big_float/GpuBigFloatArrayView.md)
