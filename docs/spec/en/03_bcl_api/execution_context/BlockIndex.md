---
title: "BlockIndex Struct (Kassyi.GSharp.Core)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Gets the block index within the grid."
---

## BlockIndex Struct

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Gets the block index within the grid.

```csharp
public readonly struct BlockIndex
```

## Properties

| Name | Description |
| --- | --- |
| [BlockIndex.X](#x) | The X coordinate/index. |
| [BlockIndex.Y](#y) | The Y coordinate/index. |
| [BlockIndex.Z](#z) | The Z coordinate/index. |

## X

The X coordinate/index.

```csharp
public static int X { get; }
```

#### Property Value

`int`  
The X coordinate/index.

## Y

The Y coordinate/index.

```csharp
public static int Y { get; }
```

#### Property Value

`int`  
The Y coordinate/index.

## Z

The Z coordinate/index.

```csharp
public static int Z { get; }
```

#### Property Value

`int`  
The Z coordinate/index.

## Remarks

Maps directly to the `blockIdx` built-in variable in CUDA PTX.
These properties are strictly designed for execution within a `[GpuKernel]` or `[GpuDeviceFunction]` on the GPU. Accessing them from the Host (CPU) execution context will throw a `Kassyi.GSharp.Core.GpuOnlyException`.

## See also

- [Execution Context](./00_index.md)
- [Gpu BCL API Overview](../00_overview.md)
