---
title: "Warp.ShuffleXor Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Exchanges a variable between threads by applying an XOR operation to their lane IDs."
---

## Warp.ShuffleXor Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Exchanges a variable between threads by applying an XOR operation to their lane IDs.

## Overloads

| Name | Description |
| --- | --- |
| [ShuffleXor(int, int, int)](#shufflexor-int-int-int) | Exchanges a 32-bit signed integer between threads by applying an XOR operation to their lane IDs. |
| [ShuffleXor(uint, int, int)](#shufflexor-uint-int-int) | Exchanges a 32-bit unsigned integer between threads by applying an XOR operation to their lane IDs. |
| [ShuffleXor(float, int, int)](#shufflexor-float-int-int) | Exchanges a single-precision floating-point number between threads by applying an XOR operation to their lane IDs. |

## ShuffleXor(int, int, int)

Exchanges a 32-bit signed integer between threads by applying an XOR operation to their lane IDs.

```csharp
public static int ShuffleXor(int var, int laneMask, int width);
```

#### Parameters

`var`
`int`
The variable to exchange.

`laneMask`
`int`
The XOR mask applied to the current lane ID.

`width`
`int`
The logical warp width (default is 32).

#### Returns

`int`
The value exchanged from the target thread.

## ShuffleXor(uint, int, int)

Exchanges a 32-bit unsigned integer between threads by applying an XOR operation to their lane IDs.

```csharp
public static uint ShuffleXor(uint var, int laneMask, int width);
```

#### Parameters

`var`
`uint`
The variable to exchange.

`laneMask`
`int`
The XOR mask applied to the current lane ID.

`width`
`int`
The logical warp width (default is 32).

#### Returns

`uint`
The value exchanged from the target thread.

## ShuffleXor(float, int, int)

Exchanges a single-precision floating-point number between threads by applying an XOR operation to their lane IDs.

```csharp
public static float ShuffleXor(float var, int laneMask, int width);
```

#### Parameters

`var`
`float`
The variable to exchange.

`laneMask`
`int`
The XOR mask applied to the current lane ID.

`width`
`int`
The logical warp width (default is 32).

#### Returns

`float`
The value exchanged from the target thread.

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
