---
title: "SharedMemory<T> Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Provides high-speed, on-chip block-local memory allocations."
---

## SharedMemory<T> Struct

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Provides high-speed, on-chip block-local memory allocations.

```csharp
public readonly ref struct SharedMemory<T> where T : unmanaged
```

## Constructors

| Name              | Description                               |
| ----------------- | ----------------------------------------- |
| SharedMemory(int) | Allocates statically-sized shared memory. |

## Properties

| Name      | Description                                      |
| --------- | ------------------------------------------------ |
| Item[int] | Gets or sets the element at the specified index. |

## SharedMemory(int)

Allocates statically-sized shared memory.

```csharp
public SharedMemory(int length);
```

#### Parameters

`length` <i>int</i><br>
The number of elements to allocate.

## Examples

### Static Allocation

```csharp
// Execution Context: G# Kernel (GPU)
using System;
using Kassyi.GSharp.Core;

try {
    // Declares block-local shared memory cache
    var cache = new SharedMemory<float>(256);
    
    // Assigning elements evaluates index and throws on CPU
    cache[0] = 3.14f;
    Console.WriteLine($"Shared memory set. Value: {cache[0]}");
} catch (GpuOnlyException) {
    Console.WriteLine("SharedMemory<T> allocations and accesses can only be executed on the GPU.");
}
```

## Remarks

Maps to `__shared__` memory in CUDA. Shared memory is dramatically faster than global memory and acts as a user-managed L1 cache for the Thread Block.

## See also



### BCL API > Gpu Class

- [Gpu Class](../gpu_class/00_index.md)
- [Gpu.SyncThreads Method (G#)](../gpu_class/SyncThreads.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)
- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../../04_guides/02_memory_safety_patterns.md)

