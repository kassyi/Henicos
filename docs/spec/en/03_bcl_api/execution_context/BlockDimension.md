---
title: "BlockDimension Struct (Kassyi.GSharp.Core)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Gets the dimensions of the thread block."
---

## BlockDimension Struct

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Gets the dimensions of the thread block.

```csharp
public readonly struct BlockDimension
```

## Properties

| Name | Description |
| --- | --- |
| [BlockDimension.X](#x) | The X dimension. |
| [BlockDimension.Y](#y) | The Y dimension. |
| [BlockDimension.Z](#z) | The Z dimension. |

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

Maps directly to the `blockDim` built-in variable in CUDA PTX.
These properties are strictly designed for execution within a `[GpuKernel]` or `[GpuDeviceFunction]` on the GPU. Accessing them from the Host (CPU) execution context will throw a `Kassyi.GSharp.Core.GpuOnlyException`.

## See also

- [Execution Context](./00_index.md)
- [Gpu BCL API Overview](../00_index.md)
