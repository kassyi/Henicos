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
| [AtomicAdd(ref long, long)](#atomicadd-ref-long-long) | Performs an atomic read-modify-write addition on a 64-bit signed integer. |
| [AtomicAdd(ref ulong, ulong)](#atomicadd-ref-ulong-ulong) | Performs an atomic read-modify-write addition on a 64-bit unsigned integer. |
| [AtomicAdd(ref float, float)](#atomicadd-ref-float-float) | Performs an atomic read-modify-write addition on a single-precision floating-point number. |
| [AtomicAdd(ref double, double)](#atomicadd-ref-double-double) | Performs an atomic read-modify-write addition on a double-precision floating-point number. |

## AtomicAdd(ref int, int)

Performs an atomic read-modify-write addition on a 32-bit signed integer.

```csharp
public static int AtomicAdd(ref int address, int val);
```

#### Parameters

`address`
`ref int`
A `ref` pointer to the memory location to be updated.

`val`
`int`
The value to add to the address.

#### Returns

`int`
The old value observed at `address` before the addition occurred.

## AtomicAdd(ref uint, uint)

Performs an atomic read-modify-write addition on a 32-bit unsigned integer.

```csharp
public static uint AtomicAdd(ref uint address, uint val);
```

#### Parameters

`address`
`ref uint`
A `ref` pointer to the memory location to be updated.

`val`
`uint`
The value to add to the address.

#### Returns

`uint`
The old value observed at `address` before the addition occurred.

## AtomicAdd(ref long, long)

Performs an atomic read-modify-write addition on a 64-bit signed integer.

```csharp
public static long AtomicAdd(ref long address, long val);
```

#### Parameters

`address`
`ref long`
A `ref` pointer to the memory location to be updated.

`val`
`long`
The value to add to the address.

#### Returns

`long`
The old value observed at `address` before the addition occurred.

## AtomicAdd(ref ulong, ulong)

Performs an atomic read-modify-write addition on a 64-bit unsigned integer.

```csharp
public static ulong AtomicAdd(ref ulong address, ulong val);
```

#### Parameters

`address`
`ref ulong`
A `ref` pointer to the memory location to be updated.

`val`
`ulong`
The value to add to the address.

#### Returns

`ulong`
The old value observed at `address` before the addition occurred.

## AtomicAdd(ref float, float)

Performs an atomic read-modify-write addition on a single-precision floating-point number.

```csharp
public static float AtomicAdd(ref float address, float val);
```

#### Parameters

`address`
`ref float`
A `ref` pointer to the memory location to be updated.

`val`
`float`
The value to add to the address.

#### Returns

`float`
The old value observed at `address` before the addition occurred.

## AtomicAdd(ref double, double)

Performs an atomic read-modify-write addition on a double-precision floating-point number.

```csharp
public static double AtomicAdd(ref double address, double val);
```

#### Parameters

`address`
`ref double`
A `ref` pointer to the memory location to be updated.

`val`
`double`
The value to add to the address.

#### Returns

`double`
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

## See also

- [SyncThreads](SyncThreads.md)
- [Gpu BCL API Overview](../01_api_overview.md)
