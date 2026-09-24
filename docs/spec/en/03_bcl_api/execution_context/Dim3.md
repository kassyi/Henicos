---
title: "Dim3 Struct (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Represents a 3-dimensional coordinate vector used for grid and block dimensions in GPU kernels."
---

## Dim3 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Represents a 3-dimensional coordinate vector used for grid and block dimensions in GPU kernels.

```csharp
public struct Dim3
```

## Properties

| Name | Description                            |
| ---- | -------------------------------------- |
| `X`  | Gets the X component of the 3D vector. |
| `Y`  | Gets the Y component of the 3D vector. |
| `Z`  | Gets the Z component of the 3D vector. |

## Examples

### Reading Coordinates

> [!NOTE] Requires assemblies: Kassyi.GSharp.Core

```csharp
// Execution Context: G# Kernel (GPU)
public class Example
{
    [GpuDeviceFunction]
    public static void ProcessCoordinate(Dim3 coord)
    {
        int x = coord.X;
        int y = coord.Y;
        int z = coord.Z;
    }
}
```

## Remarks

This struct is a marker intercepted by the GSharp Transpiler and mapped to CUDA `dim3`.
It is primarily returned by properties in `ThreadIndex`, `BlockIndex`, `BlockDimension`, and `GridDimension` when the full 3D vector is required.

## See also

### BCL API > Execution Context

- [Execution Context](00_index.md)
- [ThreadIndex](ThreadIndex.md)
- [BlockIndex](BlockIndex.md)
- [BlockDimension](BlockDimension.md)
- [GridDimension](GridDimension.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
