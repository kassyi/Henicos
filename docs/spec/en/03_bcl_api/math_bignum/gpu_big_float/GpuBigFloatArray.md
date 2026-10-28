---
title: "GpuBigFloatArray Struct (G#)"
site: "G# Documentation"
published: "2026-10-28T00:00:00.000Z"
language: "en"
description: "Represents a contiguous array of arbitrary-precision signed floating-point numbers stored in NVIDIA GPU global device memory."
---

## GpuBigFloatArray Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a contiguous array of arbitrary-precision signed floating-point numbers stored in NVIDIA GPU global device memory. Each element is $\pm\text{Significand} \times 2^{\text{Exponent}}$ with a significand of `WordsPerElement` words; the significands, the exponents and the signs live in three parallel `GpuBigUIntArray` buffers.

```csharp
public readonly struct GpuBigFloatArray : IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigFloatArray(GpuWorkspace, int, int)](#gpubigfloatarraygpuworkspace-int-int) | Initializes a new instance of the `GpuBigFloatArray` struct allocating uninitialized GPU device memory. |

## GpuBigFloatArray(GpuWorkspace, int, int)

Initializes a new instance of the `GpuBigFloatArray` struct allocating uninitialized GPU device memory for the significands, the exponents and the signs. None of the buffers is cleared; write an element with `SetElement` before reading it.

```csharp
public GpuBigFloatArray(GpuWorkspace workspace, int length, int wordsPerElement);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory allocations.

`length` _int_<br>
The number of elements in the array.

`wordsPerElement` _int_<br>
The number of 32-bit significand words per element.

## Properties

| Name | Description |
| --- | --- |
| [Exponents](#exponents) | Gets the underlying contiguous array of base-2 exponents (1 word per element, the two's complement bits of an `int`). |
| [Length](#length) | Gets the length of the array (number of elements). |
| [Significands](#significands) | Gets the underlying contiguous array of unsigned significand magnitudes. |
| [Signs](#signs) | Gets the underlying contiguous array of sign indicators (1 word per element: 0 = positive, 1 = negative). |
| [WordsPerElement](#wordsperelement) | Gets the number of 32-bit significand words per element. |
| [Workspace](#workspace) | Gets the `GpuWorkspace` associated with this array. |

## Exponents

Gets the underlying contiguous array of base-2 exponents (1 32-bit word per element, stored as the two's complement bits of an `int`).

```csharp
public GpuBigUIntArray Exponents { get; }
```

## Length

Gets the length of the array (number of elements).

```csharp
public int Length { get; }
```

## Significands

Gets the underlying contiguous array of unsigned significand magnitudes.

```csharp
public GpuBigUIntArray Significands { get; }
```

## Signs

Gets the underlying contiguous array of sign indicators (1 word per element: 0 = positive, 1 = negative).

```csharp
public GpuBigUIntArray Signs { get; }
```

## WordsPerElement

Gets the number of 32-bit significand words per element.

```csharp
public int WordsPerElement { get; }
```

## Workspace

Gets the `GpuWorkspace` associated with this array.

```csharp
public GpuWorkspace Workspace { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Releases the GPU global device memory resources allocated for the underlying buffers. |
| [GetElement(int)](#getelementint) | Gets a `GpuBigFloatView` of the specified element (reads its exponent and sign back to the host). |
| [SetElement(int, GpuBigFloatView)](#setelementint-gpubigfloatview) | Stores a value into the specified element, rounding a wider significand toward zero. |
| [Slice(int, int)](#sliceint-int) | Creates a borrowed view of a range of the elements. |

## Dispose()

Releases the GPU global device memory resources allocated for the significand, exponent and sign buffers.

```csharp
public void Dispose();
```

## GetElement(int)

Gets a view of the specified element: a view of its significand in the array's device memory, with its exponent and sign read back to the host.

> [!CAUTION]
> Reading the exponent and the sign back to the host is synchronous. The view lives only inside the calling method (a `ref struct`) and must not be used after the array is disposed.

```csharp
public GpuBigFloatView GetElement(int index);
```

#### Parameters

`index` _int_<br>
The zero-based index of the element.

#### Returns

_[GpuBigFloatView](GpuBigFloatView.md)_<br>
A borrowed view of the element's significand with its exponent and sign.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`index` is outside the array.

## SetElement(int, GpuBigFloatView)

Stores a value into the element at `index`.

```csharp
public void SetElement(int index, scoped GpuBigFloatView value);
```

#### Parameters

`index` _int_<br>
The target element index.

`value` _[GpuBigFloatView](GpuBigFloatView.md)_<br>
The value to store; a `GpuBigFloat` converts implicitly.

#### Exceptions

`OverflowException`  
A significand wider than the element is rounded and the exponent of the rounded value does not fit in `int`.

#### Remarks

A significand of `WordsPerElement` words or fewer is copied as it is (zero-extended) with the exponent and the sign, so the element holds the value exactly. A wider significand is rounded toward zero to $p = 32 \cdot \text{WordsPerElement}$ bits, like the other `GpuBigFloat` operations ([IGpuBigFloatArithmetic.Normalize](IGpuBigFloatArithmetic.md#normalizeref-gpubigintexecutioncontext-in-gpufloatoperand-gpubigintmemoryhandle)): its most significant bit moves to the top of the element, the bits below the element are dropped, and the exponent grows by the same shift. A wide value whose top words are zero therefore keeps every bit, and a wide zero is stored as positive zero. Storing a wide value reads its bit length back to the host once.

## Slice(int, int)

Creates a borrowed view of a range of the elements. The view shares the array's memory, lives only inside the calling method (a `ref struct`) and must not be used after the array is disposed.

```csharp
public GpuBigFloatArrayView Slice(int elementOffset, int elementCount);
```

#### Parameters

`elementOffset` _int_<br>
The starting element index of the slice.

`elementCount` _int_<br>
The number of elements in the slice.

#### Returns

_[GpuBigFloatArrayView](GpuBigFloatArrayView.md)_<br>
A borrowed view of the elements.

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown when `elementOffset < 0`, `elementCount < 0`, or `elementOffset + elementCount > Length`.

## Examples

### Storing a Wide Value into a Narrow Element

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

// Two elements of 4 words (128-bit significands)
using var array = new GpuBigFloatArray(workspace, length: 2, wordsPerElement: 4);

// 2^31 written as a 6-word significand whose only set bit is its top bit, times 2^-160
using var wide = new GpuBigFloat(new GpuBigUInt(workspace, [0u, 0u, 0u, 0u, 0u, 0x8000_0000u]), -160, false);
array.SetElement(0, wide);

// The element holds 2^127 * 2^-96 = 2^31: the top bit moved to the top of the 4 words
var element = array.GetElement(0);
Console.WriteLine($"Significand bits: {element.Significand.GetBitLength()}, Exponent: {element.Exponent}");
```

## See also

### BCL API > Math Bignum
- [GpuBigFloat Struct](GpuBigFloat.md)
- [GpuBigFloatView Struct](GpuBigFloatView.md)
- [GpuBigFloatArrayView Struct](GpuBigFloatArrayView.md)
- [IGpuBigFloatArithmetic Interface](IGpuBigFloatArithmetic.md)
- [GpuBigIntArray Struct](../gpu_big_int_and_uint/GpuBigIntArray.md)
- [Arbitrary-Precision Floats (GpuBigFloat)](00_index.md)
