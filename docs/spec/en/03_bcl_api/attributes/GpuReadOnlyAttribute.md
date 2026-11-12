---
title: "GpuReadOnlyAttribute Class (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Marks a kernel parameter, field, or property to be emitted with the const qualifier in the generated CUDA C++ source."
---

## GpuReadOnlyAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a kernel parameter, field, or property to be emitted with the `const` qualifier in the generated CUDA C++ source.

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class GpuReadOnlyAttribute : Attribute
```

## Examples

### Utilizing Constant Memory Caches

> [!NOTE] Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Transpiler Inspection

Example.Run();

public class Example
{
    [GpuKernel]
    public static unsafe void ProcessData(float* data, [GpuReadOnly] float scaleFactor)
    {
        // On CPU, ThreadIndex.X throws GpuOnlyException.
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(ProcessData));
        var param = method.GetParameters()[1];
        var isGpuReadOnly = param.GetCustomAttribute<GpuReadOnlyAttribute>() != null;

        Console.WriteLine($"Parameter '{param.Name}' has [GpuReadOnly] attribute: {isGpuReadOnly}");
    }
}
```

## Remarks

Helps the NVIDIA CUDA compiler (`nvcc`) optimize register allocation and place constant values into read-only constant memory caches.

### Comparison with [GpuConstexpr]

While both attributes optimize constant values, their underlying mechanics differ:

- **`[GpuReadOnly]` is like an `in` parameter or `readonly` field:** The parameter physically exists in memory and is passed as an argument, but the compiler qualifies it with `const`, allowing it to be placed in a fast, read-only constant cache.
- **[[GpuConstexpr]](ConstantAttribute.md) marks a compile-time constant:** It marks a kernel parameter whose value is meant to be fixed at compile time. Its candidate values are not used yet: the parameter is still passed as an ordinary runtime argument. A later release compiles one `constexpr` specialization per candidate value.

#### [GpuReadOnly]

Emits the standard `const` keyword in C++. The variable remains in the C++ method signature and occupies fast, read-only constant memory caches on the GPU. It is ideal for protecting state and guiding general compiler optimizations.

**C# Source:**

```csharp
public static unsafe void ProcessData(float* data, [GpuReadOnly] float scaleFactor)
```

**Emitted CUDA C++:**

```cpp
__global__ void ProcessData(float* data, const float scaleFactor)
```

#### [[GpuConstexpr]](ConstantAttribute.md)

Marks a kernel parameter as a compile-time constant. The candidate values (`10` below) are not used yet, and `iterations` is passed at runtime like any other argument; see [GpuConstexprAttribute](ConstantAttribute.md).

**C# Source:**

```csharp
public static unsafe void MultiplyKernel(int* data, [GpuConstexpr(10)] int iterations)
```

## See also

### BCL API > Attributes

- [Attributes](00_index.md)
- [GpuConstexprAttribute](ConstantAttribute.md)
- [GpuKernelAttribute](GpuKernelAttribute.md)

### BCL API > Gpu Class

- [Gpu.Hint.Unroll](../gpu_class/HintUnroll.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
