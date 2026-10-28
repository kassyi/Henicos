---
title: "Gpu.SyncThreads Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Synchronizes all threads in a thread block."
---

## Gpu.SyncThreads Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Synchronizes all threads in a thread block.

## Overloads

| Name | Description |
| --- | --- |
| [SyncThreads()](#syncthreads-) | Synchronizes all threads in a thread block. |

## SyncThreads()

Synchronizes all threads in a thread block.

```csharp
public static void SyncThreads();
```

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU outside of a managed execution context.

## Examples

### Shared Memory Synchronization

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU)

try {
    // Synchronizes all threads in the current block barrier
    Gpu.SyncThreads();
    Console.WriteLine("All threads in block synchronized.");
} catch (GpuOnlyException) {
    Console.WriteLine("Gpu.SyncThreads can only be executed on the GPU.");
}
```

## Remarks

Maps to `__syncthreads()`. Acts as a barrier at which all threads in the block must wait before any is allowed to proceed. Crucial for avoiding read-after-write (RAW) hazards in Shared Memory.

On the CPU paths (the native CPU library and managed execution) the threads of a block run one step at a time, from one barrier or warp shuffle to the next. A thread that reaches `Gpu.SyncThreads` waits there until every thread of its block that has not finished has reached it, also when the other threads need more steps to get there (for example a warp shuffle that only some warps run). A thread in the middle of a shuffle is not waiting, so it goes on until it reaches the barrier.

## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)

### BCL API > Memory

- [Memory Management](../memory/00_index.md)
- [SharedMemory<T> Class (G#)](../memory/SharedMemory.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)

