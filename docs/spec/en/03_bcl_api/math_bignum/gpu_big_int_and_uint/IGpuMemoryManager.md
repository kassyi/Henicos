---
title: "IGpuMemoryManager Interface (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Defines the memory allocation and lifetime management contract for large-integer device buffers."
---

## IGpuMemoryManager Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines the memory allocation and lifetime management contract for large-integer device buffers.

```csharp
public interface IGpuMemoryManager
```

## Methods

| Name | Description |
| --- | --- |
| [Allocate(int)](#allocateint) | Allocates a persistent device memory handle spanning the requested number of 32-bit words. |
| [AllocateTemp(int)](#allocatetempint) | Allocates a temporary, scope-bound device memory segment that is automatically recycled or freed upon disposal. |
| [Free(GpuBigIntMemoryHandle)](#freegpubigintmemoryhandle) | Releases a previously allocated device memory handle back to the pool or driver. |

## Allocate(int)

Allocates a persistent device memory handle spanning the requested number of 32-bit words.

```csharp
GpuBigIntMemoryHandle Allocate(int numWords);
```

#### Parameters

`numWords` _int_<br>
The number of 32-bit limbs to allocate.

#### Returns

_GpuBigIntMemoryHandle_<br>
A `GpuBigIntMemoryHandle` representing the allocated device memory segment.

## AllocateTemp(int)

Allocates a temporary, scope-bound device memory segment that is automatically recycled or freed upon disposal.

```csharp
ScopedBigIntMemory AllocateTemp(int numWords);
```

#### Parameters

`numWords` _int_<br>
The number of 32-bit limbs required for temporary scratch space.

#### Returns

_ScopedBigIntMemory_<br>
A `ScopedBigIntMemory` instance managing the temporary allocation lifetime.

## Free(GpuBigIntMemoryHandle)

Releases a previously allocated device memory handle back to the pool or driver.

```csharp
void Free(GpuBigIntMemoryHandle handle);
```

#### Parameters

`handle` _GpuBigIntMemoryHandle_<br>
The memory handle to release.

## Remarks

Implementations allocate and manage GPU device memory or pinned host pools used for arbitrary-precision arithmetic. Temporary scratch allocations return a `ScopedBigIntMemory` stack-bound `ref struct` to ensure deterministic lifetime without garbage collection overhead.

## Examples

### Allocating Scoped Temporary Device Memory

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(4096, GpuExecutionEngine.GSharpAot);
IGpuMemoryManager memoryManager = workspace.MemoryManager;

// Allocate a scoped temporary buffer that automatically returns to the pool at scope exit
using ScopedBigIntMemory tempMemory = memoryManager.AllocateTemp(numWords: 8);

Console.WriteLine($"Allocated temporary handle with {tempMemory.Handle.NumWords} words.");
```

## See also

### BCL API > Math Bignum
- [GpuWorkspace Class](../gpu_workspace/GpuWorkspace.md)
- [GpuBigIntExecutionContext Struct](GpuBigIntExecutionContext.md)
- [IGpuBigIntArithmetic Interface](IGpuBigIntArithmetic.md)
- [Arbitrary-Precision Integers (GpuBigInt)](00_index.md)
