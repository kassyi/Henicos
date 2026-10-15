---
title: "ScopedGpuMemory Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A disposable stack-only ref struct representing a scoped allocation of GPU global device memory."
---

## ScopedGpuMemory Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A disposable stack-only ref struct representing a scoped allocation of GPU global device memory.

```csharp
public ref struct ScopedGpuMemory(IGpuExecutionProvider provider, int numWords)
```

## Constructors

| Name | Description |
| --- | --- |
| [ScopedGpuMemory(IGpuExecutionProvider, int)](#scopedgpumemoryigpuexecutionprovider-int) | Allocates a scoped GPU memory buffer of the specified word capacity. |

## ScopedGpuMemory(IGpuExecutionProvider, int)

Allocates a scoped GPU memory buffer of the specified word capacity.

```csharp
public ScopedGpuMemory(IGpuExecutionProvider provider, int numWords);
```

#### Parameters

`provider` _IGpuExecutionProvider_<br>
The arithmetic execution provider managing GPU allocations.

`numWords` _int_<br>
The number of 32-bit words (limbs) to allocate.

#### Exceptions

`CudaException`<br>
Thrown when device memory allocation fails (`cuMemAlloc_v2` fails due to insufficient VRAM).

`ArgumentNullException`<br>
Thrown if `provider` is `null`.

## Properties

| Name | Description |
| --- | --- |
| [Pointer](#pointer) | Gets the raw native GPU device pointer (<c>CUdeviceptr</c>). |

## Pointer

Gets the raw native GPU device pointer (<c>CUdeviceptr</c>).

```csharp
public IntPtr Pointer { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Frees the allocated GPU memory buffer. |

## Dispose()

Frees the allocated GPU memory buffer.

```csharp
public void Dispose();
```

## Remarks

Ensures deterministic deallocation of temporary GPU memory buffers upon exiting the using scope.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../../../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../../../../02_architecture/01_aot_transpiler.md)


## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

// Initialize GPU workspace
using var workspace = new GpuWorkspace(maxWords: 1024, engine: GpuExecutionEngine.GSharpAot);
var provider = workspace.Provider;

// Create a scoped GPU memory allocation
using (var memory = new ScopedGpuMemory(provider, numWords: 256))
{
    Console.WriteLine($"Allocated 256 words at device pointer: 0x{memory.Pointer:X}");
} // memory is automatically freed here via Dispose()

Console.WriteLine("ScopedGpuMemory successfully disposed.");
```
