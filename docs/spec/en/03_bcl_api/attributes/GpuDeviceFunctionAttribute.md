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
public class GpuDeviceFunctionAttribute : Attribute

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuInlineAttribute : Attribute

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuNoInlineAttribute : Attribute
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
    [GpuInline]
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

Methods marked with `[GpuDeviceFunction]` are mapped to `__device__` functions in CUDA C++. They can only be called by `[GpuKernel]` methods or other `[GpuDeviceFunction]` methods.

### Inlining Control (`[GpuInline]` and `[GpuNoInline]`)

- **`[GpuInline]` (`GpuInlineAttribute`)**: Requests force-inlining (`__forceinline__`) of the annotated device function in the transpiled CUDA C++ code.
- **`[GpuNoInline]` (`GpuNoInlineAttribute`)**: Prevents inlining (`__noinline__`) of the annotated device function in the transpiled CUDA C++ code.

## See also

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)
- [5. Limitations and Diagnostics](../../01_language/05_limitations.md)

### BCL API > Math Primitives

- [Math and BitOperations](../math_primitives/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../04_guides/08_advanced_custom_math_kernels.md)
