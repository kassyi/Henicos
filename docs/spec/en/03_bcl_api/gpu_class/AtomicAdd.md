---
title: "Gpu.AtomicAdd Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Performs an atomic read-modify-write addition to a memory address."
---

## Gpu.AtomicAdd Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Performs an atomic read-modify-write addition to a memory address.

## Overloads

| Name | Description |
| --- | --- |
| [AtomicAdd(ref int, int)](#atomicadd-ref-int-int) | Performs an atomic read-modify-write addition on a 32-bit signed integer. |
| [AtomicAdd(ref uint, uint)](#atomicadd-ref-uint-uint) | Performs an atomic read-modify-write addition on a 32-bit unsigned integer. |
| [AtomicAdd(ref ulong, ulong)](#atomicadd-ref-ulong-ulong) | Performs an atomic read-modify-write addition on a 64-bit unsigned integer. |
| [AtomicAdd(ref float, float)](#atomicadd-ref-float-float) | Performs an atomic read-modify-write addition on a single-precision floating-point number. |

## AtomicAdd(ref int, int)

Performs an atomic read-modify-write addition on a 32-bit signed integer.

```csharp
[GpuKernel]
public static int AtomicAdd(ref int address, int val);
```

#### Parameters

`address` <i>ref int</i><br>
A `ref` pointer to the memory location to be updated.

`val` <i>int</i><br>
The value to add to the address.

#### Returns

<i>int</i><br>
The old value observed at `address` before the addition occurred.

## AtomicAdd(ref uint, uint)

Performs an atomic read-modify-write addition on a 32-bit unsigned integer.

```csharp
public static uint AtomicAdd(ref uint address, uint val);
```

#### Parameters

`address` <i>ref uint</i><br>
A `ref` pointer to the memory location to be updated.

`val` <i>uint</i><br>
The value to add to the address.

#### Returns

<i>uint</i><br>
The old value observed at `address` before the addition occurred.



## AtomicAdd(ref ulong, ulong)

Performs an atomic read-modify-write addition on a 64-bit unsigned integer.

```csharp
public static ulong AtomicAdd(ref ulong address, ulong val);
```

#### Parameters

`address` <i>ref ulong</i><br>
A `ref` pointer to the memory location to be updated.

`val` <i>ulong</i><br>
The value to add to the address.

#### Returns

<i>ulong</i><br>
The old value observed at `address` before the addition occurred.

## AtomicAdd(ref float, float)

Performs an atomic read-modify-write addition on a single-precision floating-point number.

```csharp
[GpuKernel]
public static float AtomicAdd(ref float address, float val);
```

#### Parameters

`address` <i>ref float</i><br>
A `ref` pointer to the memory location to be updated.

`val` <i>float</i><br>
The value to add to the address.

#### Returns

<i>float</i><br>
The old value observed at `address` before the addition occurred.



## Examples

### Histogram Generation

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static unsafe void HistogramKernel(int* data, int* bins)
{
    int value = data[ThreadIndex.X];
    // Atomically increment the bin counter to prevent data loss
    Gpu.AtomicAdd(ref bins[value], 1); 
}
```

## Remarks

Maps to `atomicAdd(&address, val)` in CUDA PTX. This intrinsic prevents race conditions when multiple threads concurrently write to the same global or shared memory location.

> [!NOTE]
> **Interlocked vs Gpu.AtomicAdd**
> 
> G# supports both `System.Threading.Interlocked` and `Gpu.AtomicAdd`. How should you choose?
> - **Use `Interlocked` (Recommended):** For standard integer atomics (e.g., `Interlocked.Add(ref myInt, 1)`). This is the C# standard, making your code highly portable and compatible with CPU fallback modes.
> - **Use `Gpu.AtomicAdd`:** When you need atomic operations on `float` or `double`. The standard C# `.NET` BCL does not support floating-point atomic additions natively via `Interlocked`, but GPU hardware does. Use `Gpu.AtomicAdd` to unlock hardware-accelerated floating-point atomics.

## See also

- [SyncThreads](SyncThreads.md)
- [Gpu BCL API Overview](../00_overview.md)
