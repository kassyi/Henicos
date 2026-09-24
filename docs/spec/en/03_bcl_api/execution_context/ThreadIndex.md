---
title: "ThreadIndex Struct (Kassyi.GSharp.Core)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Gets the thread index within the block."
---

## ThreadIndex Struct

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Gets the thread index within the block.

```csharp
public static class ThreadIndex
```

## Properties

| Name | Description |
| --- | --- |
| [ThreadIndex.X](#x) | The X coordinate/index. |
| [ThreadIndex.Y](#y) | The Y coordinate/index. |
| [ThreadIndex.Z](#z) | The Z coordinate/index. |

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

## Examples

### Reading Thread Indices

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
        // On CPU, accessing ThreadIndex.X throws GpuOnlyException.
        // The G# Transpiler maps this class by inspecting its [CudaIntrinsicPattern] attribute.
        var type = typeof(ThreadIndex);
        var attr = type.GetCustomAttribute<CudaIntrinsicPatternAttribute>();
        
        if (attr != null)
        {
            // Will output: threadIdx.{0}
            Console.WriteLine($"ThreadIndex maps to CUDA intrinsic: {attr.Pattern}");
        }
    }
}
```

## Remarks

Maps directly to the `threadIdx` built-in variable in CUDA PTX.
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

