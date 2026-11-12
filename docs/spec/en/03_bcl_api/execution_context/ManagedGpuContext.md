---
title: "ManagedGpuContext Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "The thread context of the managed CPU path, which the code G# generates and the Core intrinsics read. For generated code only."
---

## ManagedGpuContext Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

The thread context of the managed CPU path, which the code G# generates and the Core intrinsics read. For generated
code only: every member is hidden from IntelliSense (`[EditorBrowsable(EditorBrowsableState.Never)]`). Launch a kernel
through its generated `Launch{Kernel}` method; to run its C# body as the reference result, launch it inside
[`GpuExecution.Use(KernelExecution.CSharp)`](GpuExecution.md); to run it on the managed path, inside
`GpuExecution.Use(KernelExecution.Managed)`.

```csharp
public readonly record struct GpuThreadContext(
    int ThreadIdxX, int ThreadIdxY, int ThreadIdxZ,
    int BlockIdxX, int BlockIdxY, int BlockIdxZ,
    int BlockDimX, int BlockDimY, int BlockDimZ,
    int GridDimX, int GridDimY, int GridDimZ);

[EditorBrowsable(EditorBrowsableState.Never)]
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
| `InitializeGrid(int, uint)` | Initializes grid execution with static shared memory byte size. |
| `InitializeGrid(int, uint, bool)` | Initializes grid execution with static shared memory size and phase execution policy. |
| `EndGrid()` | Releases the dynamic shared memory of the blocks at the end of a grid; the next grid allocates its own. |
| [AllocateSharedMemory\<T\>(ulong, int)](#allocatesharedmemorytulong-int) | Returns the shared memory of a `SharedMemory<T>` declaration in the current block. |
| `AllocateDynamicSharedMemory<T>(ulong, int, string)` | Allocates dynamic shared memory at a specific declaration site. |
| `FindDynamicSharedMemory<T>(ulong)` | Finds previously allocated dynamic shared memory for a declaration site. |
| `ResetWarpExchange()` | Resets the warp shuffle and rendezvous buffers. |
| `MarkWarpLaneExited(int)` | Marks a specific thread lane as exited from warp execution. |
| `WarpCanRun(int, int)` | Checks whether a warp thread lane can execute in the given round. |
| `WarpStall(int)` | Returns an exception indicating an unresolved warp deadlock or stall. |

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

A CUDA `__shared__` variable has static storage: one buffer per declaration per block, shared by every call of the declaring function and by every thread of the block. The buffer is therefore looked up by the declaration's key and element type, not by the order of allocations, which differs between a caller and the device functions it calls. A generic device function instantiated with different element types gets one region per instantiation, as each template instantiation has its own `__shared__` array on CUDA.

A region does not move when other declarations are allocated, so a span handed to one thread stays valid while other threads declare further shared memory. A later request for the same declaration may be smaller (it receives the start of the region) or larger: a larger request, such as a device function called again with a larger dynamic size, grows the region and keeps its contents. Only that declaration's region moves, and generated code declares a `SharedMemory<T>` again after every barrier, so no thread keeps a span into the old storage. See [One Buffer per Declaration per Block](../memory/SharedMemory.md#one-buffer-per-declaration-per-block).

## Remarks

`ManagedGpuContext` underpins G#'s Bulk Synchronous Parallel (BSP) CPU fallback scheduler. The generated managed launcher sets the context of each GPU thread before it steps it; user code does not set it. When `IsActive` is `true` on the current thread, device intrinsics such as [`ThreadIndex`](ThreadIndex.md), [`BlockIndex`](BlockIndex.md), [`SharedMemory<T>`](../memory/SharedMemory.md), and [`Gpu.SyncThreads`](../gpu_class/SyncThreads.md) operate against thread-local emulated state instead of throwing `GpuOnlyException`. The members stay public because the generated code, which is part of the user's assembly, writes them; `[EditorBrowsable(EditorBrowsableState.Never)]` only hides them from IntelliSense, so code that calls them itself is not prevented, and G# does not support it. The class provides supporting APIs for grid lifecycle (`InitializeGrid`, `EndGrid`), dynamic/static shared memory management (`AllocateSharedMemory`, `AllocateDynamicSharedMemory`, `FindDynamicSharedMemory`), and warp execution synchronization (`ResetWarpExchange`, `MarkWarpLaneExited`, `WarpCanRun`, `WarpStall`).

## See also

### BCL API > Execution Context

- [Execution Context](00_index.md)
- [GpuExecution and KernelExecution](GpuExecution.md)
- [ThreadIndex](ThreadIndex.md)
- [BlockIndex](BlockIndex.md)
- [BlockDimension](BlockDimension.md)
- [GridDimension](GridDimension.md)

### BCL API > Memory

- [SharedMemory<T> Struct (G#)](../memory/SharedMemory.md)
