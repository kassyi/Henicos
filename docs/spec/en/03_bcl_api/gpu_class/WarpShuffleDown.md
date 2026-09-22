---
title: "Warp.ShuffleDown Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Exchanges a variable by shifting data down by a specified delta."
---

## Warp.ShuffleDown Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Exchanges a variable by shifting data down by a specified delta.

## Overloads

| Name | Description |
| --- | --- |
| [ShuffleDown(int, uint, int)](#shuffledown-int-uint-int) | Exchanges a 32-bit signed integer by shifting data down by a specified delta. |
| [ShuffleDown(uint, uint, int)](#shuffledown-uint-uint-int) | Exchanges a 32-bit unsigned integer by shifting data down by a specified delta. |
| [ShuffleDown(float, uint, int)](#shuffledown-float-uint-int) | Exchanges a single-precision floating-point number by shifting data down by a specified delta. |

## ShuffleDown(int, uint, int)

Exchanges a 32-bit signed integer by shifting data down by a specified delta.

```csharp
public static int ShuffleDown(int var, uint delta, int width);
```

#### Parameters

`var`
`int`
The variable to exchange.

`delta`
`uint`
The offset to shift the lane ID down by.

`width`
`int`
The logical warp width (default is 32).

#### Returns

`int`
The value exchanged from the source thread.

## ShuffleDown(uint, uint, int)

Exchanges a 32-bit unsigned integer by shifting data down by a specified delta.

```csharp
public static uint ShuffleDown(uint var, uint delta, int width);
```

#### Parameters

`var`
`uint`
The variable to exchange.

`delta`
`uint`
The offset to shift the lane ID down by.

`width`
`int`
The logical warp width (default is 32).

#### Returns

`uint`
The value exchanged from the source thread.

## ShuffleDown(float, uint, int)

Exchanges a single-precision floating-point number by shifting data down by a specified delta.

```csharp
public static float ShuffleDown(float var, uint delta, int width);
```

#### Parameters

`var`
`float`
The variable to exchange.

`delta`
`uint`
The offset to shift the lane ID down by.

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
