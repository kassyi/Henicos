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
| [X](#x)  | Gets the X component of the 3D vector. |
| [Y](#y)  | Gets the Y component of the 3D vector. |
| [Z](#z)  | Gets the Z component of the 3D vector. |

## X

Gets the X component of the 3D vector.

```csharp
public int X { get; }
```

#### Property Value

`int`  
The X component.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Y

Gets the Y component of the 3D vector.

```csharp
public int Y { get; }
```

#### Property Value

`int`  
The Y component.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Z

Gets the Z component of the 3D vector.

```csharp
public int Z { get; }
```

#### Property Value

`int`  
The Z component.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Examples

### Reading Coordinates

> [!NOTE] Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Transpiler Inspection

Example.Run();

public class Example
{
    [GpuDeviceFunction]
    public static void ProcessCoordinate(Dim3 coord)
    {
        // On CPU, coord.X throws GpuOnlyException
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(ProcessCoordinate));
        var isGpuFunc = method.GetCustomAttribute<GpuDeviceFunctionAttribute>() != null;
        var paramType = method.GetParameters()[0].ParameterType;

        Console.WriteLine($"Is GPU Function: {isGpuFunc}");
        Console.WriteLine($"Parameter Type: {paramType.Name}");
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
