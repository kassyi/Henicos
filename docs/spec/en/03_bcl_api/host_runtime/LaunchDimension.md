---
title: "LaunchDimension Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides utility methods for calculating GPU kernel launch grid and block dimensions."
---

## LaunchDimension Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Provides utility methods for calculating GPU kernel launch grid and block dimensions.

```csharp
public static class LaunchDimension
```

## Methods

| Name | Description |
| --- | --- |
| [Calculate1D(Int32, Int32)](#calculate1dint32-int32) | Calculates the 1D grid and block dimensions required to execute the specified number of total threads. |

## Calculate1D(Int32, Int32)

Calculates the 1D grid and block dimensions required to execute the specified number of total threads.

```csharp
public static (uint Grid, uint Block) Calculate1D(int totalThreads, int threadsPerBlock = 256);
```

#### Parameters

`totalThreads` _Int32_<br>
The total number of threads to dispatch.

`threadsPerBlock` _Int32_<br>
The number of threads per block. Defaults to `256`.

#### Returns

_(UInt32 Grid, UInt32 Block)_<br>
A tuple containing the calculated grid dimension and block dimension. When `totalThreads` is `0`, returns `(0, 0)`.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`totalThreads` is negative, or `threadsPerBlock` is less than or equal to zero, or `threadsPerBlock` exceeds the hardware limit of `1024`.

## Examples

### Calculating 1D Kernel Launch Dimensions

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime;

// Execution Context: Host (CPU)
var (grid, block) = LaunchDimension.Calculate1D(totalThreads: 1000, threadsPerBlock: 256);
Console.WriteLine($"Grid: {grid}, Block: {block}");
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [GpuKernelLoader Class](GpuKernelLoader.md)
