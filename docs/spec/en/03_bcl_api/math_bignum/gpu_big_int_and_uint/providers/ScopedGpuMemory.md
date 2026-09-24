---
title: "ScopedGpuMemory Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A disposable stack-only ref struct representing a scoped allocation of GPU global device memory."
---

## ScopedGpuMemory Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

A disposable stack-only ref struct representing a scoped allocation of GPU global device memory.

```csharp
public ref struct ScopedGpuMemory(IGpuExecutionProvider provider, int numWords)
```

## Properties

| Name                | Description                                                  |
| ------------------- | ------------------------------------------------------------ |
| [Pointer](#pointer) | Gets the raw native GPU device pointer (<c>CUdeviceptr</c>). |

## Pointer

Gets the raw native GPU device pointer (<c>CUdeviceptr</c>).

```csharp
public IntPtr Pointer { get; }
```

## Methods

| Name                  | Description                            |
| --------------------- | -------------------------------------- |
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
- [GpuContext](../../../../03_bcl_api/math_bignum/gpu_context/GpuContext.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../../../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../../../../02_architecture/01_aot_transpiler.md)


## Examples

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt.Providers;

// Initialize GPU context
using var context = new GpuContext(maxWords: 1024, engine: GpuExecutionEngine.GSharpAot);
var provider = context.Provider;

// Create a scoped GPU memory allocation
using (var memory = new ScopedGpuMemory(provider, numWords: 256))
{
    Console.WriteLine($"Allocated 256 words at device pointer");
} // memory is automatically freed here via Dispose()

Console.WriteLine("ScopedGpuMemory successfully disposed.");
```
