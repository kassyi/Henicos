---
title: "GridDimension Struct (Kassyi.GSharp.Core)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Gets the dimensions of the grid."
---

## GridDimension Struct

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Gets the dimensions of the grid.

```csharp
public readonly struct GridDimension
```

## Properties

| Name | Description |
| --- | --- |
| [GridDimension.X](#x) | The X dimension. |
| [GridDimension.Y](#y) | The Y dimension. |
| [GridDimension.Z](#z) | The Z dimension. |

## X

The X dimension.

```csharp
public static int X { get; }
```

#### Property Value

`int`  
The X dimension.

## Y

The Y dimension.

```csharp
public static int Y { get; }
```

#### Property Value

`int`  
The Y dimension.

## Z

The Z dimension.

```csharp
public static int Z { get; }
```

#### Property Value

`int`  
The Z dimension.

## Remarks

Maps directly to the `gridDim` built-in variable in CUDA PTX.
These properties are strictly designed for execution within a `[GpuKernel]` or `[GpuDeviceFunction]` on the GPU. Accessing them from the Host (CPU) execution context will throw a `Kassyi.GSharp.Core.GpuOnlyException`.

## See also

- [Execution Context](./00_index.md)
- [Gpu BCL API Overview](../01_api_overview.md)
