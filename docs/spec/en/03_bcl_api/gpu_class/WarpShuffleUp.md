---
title: "Warp.ShuffleUp Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Exchanges a variable by shifting data up by a specified delta."
---

## Warp.ShuffleUp Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Exchanges a variable by shifting data up by a specified delta.

## Overloads

| Name | Description |
| --- | --- |
| [ShuffleUp(int, uint, int)](#shuffleup-int-uint-int) | Exchanges a 32-bit signed integer by shifting data up by a specified delta. |
| [ShuffleUp(uint, uint, int)](#shuffleup-uint-uint-int) | Exchanges a 32-bit unsigned integer by shifting data up by a specified delta. |
| [ShuffleUp(float, uint, int)](#shuffleup-float-uint-int) | Exchanges a single-precision floating-point number by shifting data up by a specified delta. |

## ShuffleUp(int, uint, int)

Exchanges a 32-bit signed integer by shifting data up by a specified delta.

```csharp
public static int ShuffleUp(int var, uint delta, int width);
```

#### Parameters

`var`
`int`
The variable to exchange.

`delta`
`uint`
The offset to shift the lane ID up by.

`width`
`int`
The logical warp width (default is 32).

#### Returns

`int`
The value exchanged from the source thread.

## ShuffleUp(uint, uint, int)

Exchanges a 32-bit unsigned integer by shifting data up by a specified delta.

```csharp
public static uint ShuffleUp(uint var, uint delta, int width);
```

#### Parameters

`var`
`uint`
The variable to exchange.

`delta`
`uint`
The offset to shift the lane ID up by.

`width`
`int`
The logical warp width (default is 32).

#### Returns

`uint`
The value exchanged from the source thread.

## ShuffleUp(float, uint, int)

Exchanges a single-precision floating-point number by shifting data up by a specified delta.

```csharp
public static float ShuffleUp(float var, uint delta, int width);
```

#### Parameters

`var`
`float`
The variable to exchange.

`delta`
`uint`
The offset to shift the lane ID up by.

`width`
`int`
The logical warp width (default is 32).

#### Returns

`float`
The value exchanged from the source thread.

## Examples

### Warp Reduction

```csharp
[GpuKernel]
public static unsafe void WarpReduceKernel(int* data)
{
    int val = data[ThreadIndex.X];
    
    // Reduce values across the warp using ShuffleDown
    for (uint offset = 16; offset > 0; offset /= 2)
    {
        val += Warp.ShuffleDown(val, offset, 32);
    }
    
    if (ThreadIndex.X % 32 == 0)
    {
        data[ThreadIndex.X] = val; // Store result
    }
}
```

## Remarks

Maps to PTX `shfl.sync` instructions. Thread synchronization across the warp is implicit, avoiding the need for `Gpu.SyncThreads()`.

## See also

- [SyncThreads](SyncThreads.md)
- [Gpu BCL API Overview](../01_api_overview.md)
