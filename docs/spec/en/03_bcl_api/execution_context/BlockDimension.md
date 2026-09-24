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
public static class BlockDimension
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

## Examples

### Reading Block Dimensions

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Transpiler Inspection

KernelExample.Run();

public class KernelExample
{
    public static void Run()
    {
        // On CPU, accessing BlockDimension.X throws GpuOnlyException.
        // The G# Transpiler maps this class by inspecting its [CudaIntrinsicPattern] attribute.
        var type = typeof(BlockDimension);
        var attr = type.GetCustomAttribute<CudaIntrinsicPatternAttribute>();
        
        if (attr != null)
        {
            // Will output: blockDim.{0}
            Console.WriteLine($"BlockDimension maps to CUDA intrinsic: {attr.Pattern}");
        }
    }
}
```

## Remarks

Maps directly to the `blockDim` built-in variable in CUDA PTX.
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

