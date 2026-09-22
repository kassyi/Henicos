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
[GpuKernel]
public static void SmoothKernel(float[] input)
{
    var cache = new SharedMemory<float>(256);
    cache[ThreadIndex.X] = input[ThreadIndex.X];
    Gpu.SyncThreads();
    // ...
}
```

## Remarks

Maps to `__shared__` memory in CUDA. Shared memory is dramatically faster than global memory and acts as a user-managed L1 cache for the Thread Block.

## See also

- [Gpu BCL API Overview](../00_index.md)
