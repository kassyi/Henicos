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

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Examples

### Shared Memory Synchronization

```csharp
// Execution Context: G# Kernel (GPU)
using System;
using Kassyi.GSharp.Core;

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

