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

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Transpiler Inspection

Example.Run();

public class Example
{
    [GpuDeviceFunction]
    public static float ComputeDelta(float a, float b)
    {
        return MathF.Abs(a - b);
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(ComputeDelta));
        var hasAttr = method.GetCustomAttribute<GpuDeviceFunctionAttribute>() != null;

        Console.WriteLine($"Method '{method.Name}' has [GpuDeviceFunction]: {hasAttr}");
    }
}
```

## Remarks

Methods marked with this attribute are mapped to `__device__` functions in CUDA C++. They can only be called by `[GpuKernel]` methods or other `[GpuDeviceFunction]` methods.

## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)
- [5. Limitations and Diagnostics](../../01_language/05_limitations.md)

### BCL API > Math Primitives

- [Math and BitOperations](../math_primitives/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInteger with Custom Algorithms](../../04_guides/08_advanced_custom_math_kernels.md)

