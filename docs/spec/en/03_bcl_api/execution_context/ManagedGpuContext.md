---
title: "ManagedGpuContext Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides a managed execution context for evaluating GPU kernels directly on the .NET host CPU without native toolchains."
---

## ManagedGpuContext Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Provides a managed execution context for evaluating GPU kernels directly on the .NET host CPU without native toolchains.

```csharp
public readonly record struct GpuThreadContext(
    int ThreadIdxX, int ThreadIdxY, int ThreadIdxZ,
    int BlockIdxX, int BlockIdxY, int BlockIdxZ,
    int BlockDimX, int BlockDimY, int BlockDimZ,
    int GridDimX, int GridDimY, int GridDimZ);

public static class ManagedGpuContext
```

## Fields

| Name | Description |
| --- | --- |
| `IsActive` | Thread-static flag indicating whether the current thread is executing within a managed cooperative scheduler. |
| `ThreadIdxX` | Thread-static X-dimension thread index within the current thread block. |
| `ThreadIdxY` | Thread-static Y-dimension thread index within the current thread block. |
| `ThreadIdxZ` | Thread-static Z-dimension thread index within the current thread block. |
| `BlockIdxX` | Thread-static X-dimension block index within the grid. |
| `BlockIdxY` | Thread-static Y-dimension block index within the grid. |
| `BlockIdxZ` | Thread-static Z-dimension block index within the grid. |
| `BlockDimX` | Thread-static X-dimension size of each block. |
| `BlockDimY` | Thread-static Y-dimension size of each block. |
| `BlockDimZ` | Thread-static Z-dimension size of each block. |
| `GridDimX` | Thread-static X-dimension size of the grid. |
| `GridDimY` | Thread-static Y-dimension size of the grid. |
| `GridDimZ` | Thread-static Z-dimension size of the grid. |

## Methods

| Name | Description |
| --- | --- |
| [InitializeGrid(int)](#initializegridint) | Initializes or resets the shared memory arrays for the entire grid execution. |
| [AllocateSharedMemory\<T\>(ulong, int)](#allocatesharedmemorytulong-int) | Returns the shared memory of a `SharedMemory<T>` declaration in the current block. |
| [SetThreadContext(in GpuThreadContext)](#setthreadcontextin-gputhreadcontext) | Sets the thread coordinates for the current cooperative scheduler step. |
| [ClearThreadContext()](#clearthreadcontext) | Clears the thread context when execution is completed. |

## InitializeGrid(int)

Initializes or resets the shared memory arrays for the entire grid execution.

```csharp
public static void InitializeGrid(int totalBlocks);
```

#### Parameters

`totalBlocks` _int_<br>
The total number of blocks in the grid.

## AllocateSharedMemory\<T\>(ulong, int)

Returns the shared memory of a [`SharedMemory<T>`](../memory/SharedMemory.md) declaration in the current block, allocating it the first time the declaration is reached in the block.

```csharp
public static Span<T> AllocateSharedMemory<T>(ulong siteKey, int count) where T : unmanaged;
```

#### Type Parameters

`T`<br>
The unmanaged element type of the shared memory buffer.

#### Parameters

`siteKey` _ulong_<br>
The key that identifies the declaration. The G# compiler computes it for generated code.

`count` _int_<br>
The number of elements to allocate.

#### Returns

_Span\<T\>_<br>
A contiguous `Span<T>` pointing to the allocated shared memory region.

#### Exceptions

`InvalidOperationException`<br>
Thrown if the grid shared memory buffers have not been initialized via `InitializeGrid`.

#### Remarks

A CUDA `__shared__` variable has static storage: one buffer per declaration per block, shared by every call of the declaring function and by every thread of the block. The buffer is therefore looked up by the declaration's key, not by the order of allocations, which differs between a caller and the device functions it calls. See [One Buffer per Declaration per Block](../memory/SharedMemory.md#one-buffer-per-declaration-per-block).

## SetThreadContext(in GpuThreadContext)

Sets the thread coordinates for the current cooperative scheduler step and activates the managed execution context (`IsActive = true`).

```csharp
public static void SetThreadContext(in GpuThreadContext ctx);
```

#### Parameters

`ctx` _GpuThreadContext_<br>
The GPU thread execution context coordinates.

## ClearThreadContext()

Clears the thread context when execution is completed (`IsActive = false`).

```csharp
public static void ClearThreadContext();
```

## Examples

### Simulating Thread Coordinates on the Host CPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Managed Emulation

ManagedGpuContext.InitializeGrid(totalBlocks: 2);
var ctx = new GpuThreadContext(
    ThreadIdxX: 3, ThreadIdxY: 0, ThreadIdxZ: 0,
    BlockIdxX: 1, BlockIdxY: 0, BlockIdxZ: 0,
    BlockDimX: 32, BlockDimY: 1, BlockDimZ: 1,
    GridDimX: 2, GridDimY: 1, GridDimZ: 1);

ManagedGpuContext.SetThreadContext(in ctx);
try
{
    int globalId = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
    Console.WriteLine($"Simulated Global Thread ID: {globalId}");
}
finally
{
    ManagedGpuContext.ClearThreadContext();
}
```

## Remarks

`ManagedGpuContext` underpins G#'s Bulk Synchronous Parallel (BSP) CPU fallback scheduler. When `IsActive` is `true` on the current thread, device intrinsics such as [`ThreadIndex`](ThreadIndex.md), [`BlockIndex`](BlockIndex.md), [`SharedMemory<T>`](../memory/SharedMemory.md), and [`Gpu.SyncThreads`](../gpu_class/SyncThreads.md) operate against thread-local emulated state instead of throwing `GpuOnlyException`.

## See also

### BCL API > Execution Context

- [Execution Context](00_index.md)
- [ThreadIndex](ThreadIndex.md)
- [BlockIndex](BlockIndex.md)
- [BlockDimension](BlockDimension.md)
- [GridDimension](GridDimension.md)

### BCL API > Memory

- [SharedMemory<T> Struct (G#)](../memory/SharedMemory.md)
