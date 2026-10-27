---
title: "GpuBigIntArray Struct (G#)"
site: "G# Documentation"
published: "2026-10-12T00:00:00.000Z"
language: "en"
description: "Represents a contiguous array of arbitrary-precision signed integers stored in NVIDIA GPU global device memory."
---

## GpuBigIntArray Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a contiguous array of arbitrary-precision signed integers stored in NVIDIA GPU global device memory. Encapsulates contiguous magnitude buffers and parallel sign arrays for vectorized calculations.

```csharp
public readonly struct GpuBigIntArray : IDisposable
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigIntArray(GpuWorkspace, int, int)](#gpubigintarraygpuworkspace-int-int) | Initializes a new instance of the `GpuBigIntArray` struct allocating GPU global device memory. |

## GpuBigIntArray(GpuWorkspace, int, int)

Initializes a new instance of the `GpuBigIntArray` struct allocating uninitialized GPU device memory for both magnitudes and sign indicators.

Neither buffer is cleared. Vector operations, including the comparisons of [GpuBigIntVectorLinqExtensions](expressions/GpuBigIntVectorLinqExtensions.md), read the sign word of each element, so write the `Signs` buffer together with `Values` when filling the array.

```csharp
public GpuBigIntArray(GpuWorkspace workspace, int length, int wordsPerElement);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory allocations.

`length` _int_<br>
The number of elements in the array.

`wordsPerElement` _int_<br>
The number of 32-bit words per element.

## Properties

| Name | Description |
| --- | --- |
| [Length](#length) | Gets the length of the array (number of elements). |
| [Signs](#signs) | Gets the underlying contiguous array of sign indicators (1 word per element: 0 = positive, 1 = negative). |
| [Values](#values) | Gets the underlying contiguous array of absolute values (magnitudes). |
| [WordsPerElement](#wordsperelement) | Gets the number of 32-bit words per element. |

## Length

Gets the length of the array (number of elements).

```csharp
public int Length { get; }
```

## Signs

Gets the underlying contiguous array of sign indicators (1 word per element: 0 = positive, 1 = negative).

```csharp
public GpuBigUIntArray Signs { get; }
```

## Values

Gets the underlying contiguous array of absolute values (magnitudes).

```csharp
public GpuBigUIntArray Values { get; }
```

## WordsPerElement

Gets the number of 32-bit words per element.

```csharp
public int WordsPerElement { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Releases the GPU global device memory resources allocated for the underlying buffers. |
| [GetElement(int)](#getelementint) | Gets a scalar representation of the specified element (incurs device-to-host read). |
| [Slice(int, int)](#sliceint-int) | Creates a zero-allocation sub-slice of this GPU array. |

## Dispose()

Releases the GPU global device memory resources allocated for the underlying magnitude and sign buffers.

```csharp
public void Dispose();
```

## GetElement(int)

Gets a scalar representation of the specified element.

> [!CAUTION]
> Reading from GPU device memory back to host is synchronous and carries high latency. This method is intended primarily for debugging and validation.

```csharp
public GpuBigInt GetElement(int index);
```

The result is borrowed: it points into this array's buffer and does not own it, so disposing it frees nothing. Do not use it after the array is disposed; its `DevicePointer` then throws `ObjectDisposedException`.

#### Parameters

`index` _int_<br>
The zero-based index of the element.

#### Returns

_[GpuBigInt](GpuBigInt.md)_<br>
A `GpuBigInt` pointing to the element's device memory.

## Slice(int, int)

Creates a zero-allocation sub-slice of this GPU array.

```csharp
public GpuBigIntArray Slice(int elementOffset, int elementCount);
```

The result is borrowed: it points into this array's buffer and does not own it, so disposing it frees nothing. Do not use it after the array is disposed; its `DevicePointer` then throws `ObjectDisposedException`.

#### Parameters

`elementOffset` _int_<br>
The starting element index of the slice.

`elementCount` _int_<br>
The number of elements in the slice.

#### Returns

_[GpuBigIntArray](GpuBigIntArray.md)_<br>
A sliced view of the `GpuBigIntArray`.

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown when `elementOffset < 0`, `elementCount < 0`, or `elementOffset + elementCount > Length`.

## Remarks

`GpuBigIntArray` coordinates two parallel `GpuBigUIntArray` instances—one containing contiguous limbs for magnitude arithmetic, and one containing compact sign flags—enabling SIMD-friendly vector kernels without branch divergences.

## Examples

### Allocating and Slicing a GPU BigInt Vector Array

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

// Allocate an array of 32 signed big integers (8 words = 256 bits each)
using var signedArray = new GpuBigIntArray(workspace, length: 32, wordsPerElement: 8);

// Create a zero-allocation 16-element sub-slice starting at index 8
GpuBigIntArray subSlice = signedArray.Slice(elementOffset: 8, elementCount: 16);

Console.WriteLine($"Signed array allocated: Length={signedArray.Length}, Slice Length={subSlice.Length}, Words={subSlice.WordsPerElement}");
```

## See also

### BCL API > Math Bignum
- [GpuBigUIntArray Struct](GpuBigUIntArray.md)
- [Arbitrary-Precision Integers (GpuBigInt)](00_index.md)
