---
title: "SharedMemory<T> Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Provides high-speed, on-chip block-local memory allocations."
---

## SharedMemory<T> Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Provides high-speed, on-chip block-local memory allocations.

```csharp
public static class SharedMemory<T> where T : unmanaged
```
## Methods

| Name | Description |
| --- | --- |
| Allocate(Int32) | Allocates statically-sized shared memory. |
| AllocateDynamic() | Allocates dynamically-sized shared memory specified at kernel launch. |

## Allocate(Int32)

Allocates statically-sized shared memory.

```csharp
public static T* Allocate(int count);
```

#### Parameters

`count`
`int`
The number of elements to allocate.

#### Returns

`T*`
A pointer to the beginning of the shared memory array.

## AllocateDynamic()

Allocates dynamically-sized shared memory specified at kernel launch.

```csharp
public static T* AllocateDynamic();
```

#### Returns

`T*`
A pointer to the beginning of the dynamically allocated shared memory array.

## Examples

### Static Allocation

```csharp
[GpuKernel]
public static unsafe void SmoothKernel(float* input)
{
    var cache = SharedMemory<float>.Allocate(256);
    cache[ThreadIndex.X] = input[ThreadIndex.X];
    Gpu.SyncThreads();
    // ...
}
```

## Remarks

Maps to `__shared__` memory in CUDA. Shared memory is dramatically faster than global memory and acts as a user-managed L1 cache for the Thread Block.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
