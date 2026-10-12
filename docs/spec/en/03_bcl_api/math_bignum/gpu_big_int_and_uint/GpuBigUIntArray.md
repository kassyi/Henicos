---
title: "GpuBigUIntArray Struct (G#)"
site: "G# Documentation"
published: "2026-10-12T00:00:00.000Z"
language: "en"
description: "Represents a contiguous array of arbitrary-precision unsigned integers stored in NVIDIA GPU global device memory."
---

## GpuBigUIntArray Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents a contiguous array of arbitrary-precision unsigned integers stored in NVIDIA GPU global device memory. Designed for high-performance batched, vectorized execution and multi-block parallel reductions with zero per-element memory allocation overhead.

```csharp
[StructLayout(LayoutKind.Sequential)]
public readonly struct GpuBigUIntArray : IDisposable
```

## Constructors

| Name                                                                                        | Description                                                                                     |
| ------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| [GpuBigUIntArray(GpuWorkspace, int, int)](#gpubiguintarraygpuworkspace-int-int)           | Initializes a new instance of the `GpuBigUIntArray` struct allocating GPU global device memory. |

## GpuBigUIntArray(GpuWorkspace, int, int)

Initializes a new instance of the `GpuBigUIntArray` struct allocating uninitialized contiguous GPU device memory from the specified workspace memory pool.

```csharp
public GpuBigUIntArray(GpuWorkspace workspace, int length, int wordsPerElement);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory pool allocations.

`length` _int_<br>
The number of big integer elements contained in the array.

`wordsPerElement` _int_<br>
The number of 32-bit unsigned limbs (words) allocated per element.

#### Exceptions

`ArgumentNullException`<br>
Thrown if `workspace` is null.

## Properties

| Name                                      | Description                                                                           |
| ----------------------------------------- | ------------------------------------------------------------------------------------- |
| [DevicePointer](#devicepointer)           | Gets the raw 64-bit device memory pointer to the first element in GPU address space.  |
| [Handle](#handle)                         | Gets the underlying GPU memory handle tracking device buffer allocation and lifetime. |
| [IsTemp](#istemp)                         | Gets a value indicating whether this buffer represents an intermediate temporary buffer. |
| [Length](#length)                         | Gets the number of elements in the array.                                             |
| [TotalWords](#totalwords)                 | Gets the total number of 32-bit words allocated for this array.                       |
| [WordsPerElement](#wordsperelement)       | Gets the number of 32-bit unsigned integer limbs per element.                         |
| [Workspace](#workspace)                   | Gets the `GpuWorkspace` associated with this GPU array instance.                      |

## DevicePointer

Gets the raw 64-bit device memory pointer to the first element in GPU global address space.

```csharp
public IntPtr DevicePointer { get; }
```

## Handle

Gets the underlying GPU memory handle tracking device buffer allocation and lifetime.

```csharp
public GpuBigIntMemoryHandle Handle { get; }
```

## IsTemp

Gets a value indicating whether this buffer represents an intermediate temporary buffer rented from the pool.

```csharp
public bool IsTemp { get; }
```

## Length

Gets the number of elements in the array.

```csharp
public int Length { get; }
```

## TotalWords

Gets the total number of 32-bit words allocated for this array (`Length * WordsPerElement`).

```csharp
public int TotalWords { get; }
```

## WordsPerElement

Gets the number of 32-bit unsigned integer limbs (words) per element.

```csharp
public int WordsPerElement { get; }
```

## Workspace

Gets the `GpuWorkspace` associated with this GPU array instance.

```csharp
public GpuWorkspace Workspace { get; }
```

## Methods

| Name                                | Description                                                                               |
| ----------------------------------- | ----------------------------------------------------------------------------------------- |
| [Dispose()](#dispose)               | Releases the GPU global device memory resources allocated for the underlying buffer.      |
| [GetElement(int)](#getelementint)   | Gets a slice representation of the specified element without allocating a new buffer.     |

## Dispose()

Releases the GPU global device memory resources allocated for the underlying buffer back to the workspace memory pool.

```csharp
public void Dispose();
```

## GetElement(int)

Gets a slice representation of the specified element pointing directly to the element's offset in GPU device memory without allocating an independent device buffer.

```csharp
public GpuBigUInt GetElement(int index);
```

#### Parameters

`index` _int_<br>
The zero-based index of the element to retrieve.

#### Returns

_GpuBigUInt_<br>
A `GpuBigUInt` instance pointing directly to the contiguous memory slice of the element.

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown if `index` is less than 0 or greater than or equal to [Length](#length).

## Remarks

`GpuBigUIntArray` provides continuous physical memory layout for large collections of big numbers, allowing CUDA kernels to achieve coalesced global memory access and enabling efficient grid-stride or chunked reductions (such as Multi-Scalar Multiplication or vector inner products).

## Examples

### Allocating and Slicing GPU Big Integer Array

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);

// Allocate contiguous array of 64 elements, each 8 words (256-bit)
using var array = new GpuBigUIntArray(workspace, length: 64, wordsPerElement: 8);

// Access a slice without allocating new GPU memory
using var firstElement = array.GetElement(0);

Console.WriteLine($"Array allocated with {array.Length} elements, total words: {array.TotalWords}");
```

## See also

### BCL API > Math Bignum
- [GpuBigIntArray Struct](GpuBigIntArray.md)
- [Arbitrary-Precision Integers (GpuBigInt)](00_index.md)
