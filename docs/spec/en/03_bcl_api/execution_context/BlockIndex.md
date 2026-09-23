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
public static class BlockIndex
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



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)

### BCL API > Gpu Class

- [Gpu Class](../gpu_class/00_index.md)
- [Gpu.GridStrideLoop Method (G#)](../gpu_class/GridStrideLoop.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [2D/3D Grid Mapping and Image Processing Patterns](../../04_guides/09_2d_grid_mapping_and_image_processing.md)

