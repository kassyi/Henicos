---
title: "GpuDeviceFunctionAttribute Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Marks a C# method to be transpiled into a GPU-callable device function."
---

## GpuDeviceFunctionAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a C# method to be transpiled into a GPU-callable device function.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class GpuDeviceFunctionAttribute : Attribute
```
## Examples

### Usage

```csharp
[GpuDeviceFunction]
public static float ComputeDelta(float a, float b)
{
    return MathF.Abs(a - b);
}
```

## Remarks

Methods marked with this attribute are mapped to `__device__` functions in CUDA C++. They can only be called by `[GpuKernel]` methods or other `[GpuDeviceFunction]` methods.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
