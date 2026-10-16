---
title: "ScopedGpuMemory Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "A disposable stack-only ref struct representing a scoped allocation of GPU global device memory."
---

## ScopedGpuMemory Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A disposable stack-only `ref struct` representing a scoped allocation of GPU global device memory.

```csharp
public ref struct ScopedGpuMemory(IGpuExecutionProvider provider, int numWords)
```

## Constructors

| Name | Description |
| --- | --- |
| [ScopedGpuMemory(IGpuExecutionProvider, Int32)](#scopedgpumemoryigpuexecutionprovider-int32) | Allocates a scoped GPU memory buffer of the specified word capacity. |

## ScopedGpuMemory(IGpuExecutionProvider, Int32)

Allocates a scoped GPU memory buffer of the specified word capacity via [`IGpuMemoryProvider.AllocateMemory(Int32)`](IGpuExecutionProvider.md#allocatememoryint32).

```csharp
public ScopedGpuMemory(IGpuExecutionProvider provider, int numWords);
```

#### Parameters

`provider` _IGpuExecutionProvider_<br>
The execution provider managing GPU device memory allocations.

`numWords` _Int32_<br>
The number of 32-bit words (limbs) to allocate.

#### Exceptions

`CudaException`<br>
Thrown when device memory allocation fails (`cuMemAlloc_v2` fails due to insufficient VRAM).

`NullReferenceException`<br>
Thrown if `provider` is `null` when invoking `AllocateMemory`.

## Properties

| Name | Description |
| --- | --- |
| [Pointer](#pointer) | Gets the raw native GPU device pointer (`CUdeviceptr`). |

## Pointer

Gets the raw native GPU device pointer (`CUdeviceptr`) allocated by the underlying provider.

```csharp
public IntPtr Pointer { get; }
```

#### Property Value

_IntPtr_<br>
The native CUDA device pointer (`CUdeviceptr`), or `IntPtr.Zero` if already disposed.

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Frees the allocated GPU memory buffer and resets `Pointer` to `IntPtr.Zero`. |

## Dispose()

Frees the allocated GPU memory buffer via [`IGpuMemoryProvider.FreeMemory(IntPtr, IntPtr)`](IGpuExecutionProvider.md#freememoryintptr-intptr) and resets `Pointer` to `IntPtr.Zero`. Calling `Dispose()` multiple times is idempotent.

```csharp
public void Dispose();
```

## Remarks

`ScopedGpuMemory` is a zero-allocation `ref struct` designed for `using var` declarations on the host stack, ensuring deterministic deallocation of temporary GPU buffers even if an exception occurs during kernel execution.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
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

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../../../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../../../../02_architecture/01_aot_transpiler.md)
