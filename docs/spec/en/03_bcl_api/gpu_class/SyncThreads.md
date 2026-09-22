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
[GpuKernel]
public static void SyncThreads();
```

## Examples

### Shared Memory Synchronization

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static unsafe void SharedMemKernel(float* input, float* output)
{
    var cache = SharedMemory<float>.Allocate(256);
    cache[ThreadIndex.X] = input[ThreadIndex.X];
    
    // Wait for all threads to finish writing to shared memory
    Gpu.SyncThreads();
    
    // Safe to read from other threads' indices now
    output[ThreadIndex.X] = cache[255 - ThreadIndex.X];
}
```

## Remarks

Maps to `__syncthreads()`. Acts as a barrier at which all threads in the block must wait before any is allowed to proceed. Crucial for avoiding read-after-write (RAW) hazards in Shared Memory.

## See also

### Synchronization & Memory
- [SharedMemory](../memory/SharedMemory.md)
- [AtomicAdd](AtomicAdd.md)
- [SingleThread](SingleThread.md)

### Warp & Concurrency Model
- [WarpShuffleDown](WarpShuffleDown.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)
- [Execution Context Overview](../execution_context/00_index.md)
- [Gpu BCL API Overview](../00_overview.md)

### Guides
- [Warp-Level Programming Guide](../../04_guides/01_warp_programming_guide.md)
- [Antipatterns and Best Practices Guide (Divergence & Deadlocks)](../../04_guides/06_antipatterns_and_best_practices.md)
