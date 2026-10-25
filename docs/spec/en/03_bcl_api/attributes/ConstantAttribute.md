---
title: "GpuConstexprAttribute Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Marks a kernel parameter as a compile-time constant."
---

## GpuConstexprAttribute Class

> [!NOTE]
> Candidate values passed to `[GpuConstexpr(...)]` are not used yet: the parameter is still passed at runtime. A later release compiles one `constexpr` specialization per value.

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a kernel parameter as a compile-time constant.

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class GpuConstexprAttribute : Attribute
{
    public GpuConstexprAttribute() { }
    public GpuConstexprAttribute(params long[] values) { }
    public GpuConstexprAttribute(params double[] values) { }
}
```

## Examples

### Loop Unrolling with GpuConstexpr

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
    public static unsafe void MultiplyKernel(int* data, [GpuConstexpr(10)] int iterations)
    {
        // On CPU, ThreadIndex.X throws GpuOnlyException.
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(MultiplyKernel));
        var param = method.GetParameters()[1];
        var isConstexpr = param.GetCustomAttribute<GpuConstexprAttribute>() != null;

        Console.WriteLine($"Parameter '{param.Name}' has [GpuConstexpr] attribute: {isConstexpr}");
    }
}
```

## Remarks

The parameter is marked for compile-time constant specialization.

### Comparison with `[GpuReadOnly]`

While both attributes optimize constant values, their underlying mechanics differ fundamentally. **If you think of this in C# terms:**

- **[GpuConstexpr] is like a `const` literal:** The variable becomes a C++ `constexpr` at compile-time (in a later release).
- **[[GpuReadOnly]](GpuReadOnlyAttribute.md) is like an `in` parameter or `readonly` field:** The variable physically exists in memory and is passed as a real argument, but the compiler ensures it cannot be modified, allowing it to be placed in a fast, read-only memory cache.

#### `[GpuConstexpr]`

Marks a kernel parameter as a compile-time constant with a listed set of candidate values.

**C# Source (called with `iterations = 10` on the CPU):**

```csharp
public static unsafe void MultiplyKernel(int* data, [GpuConstexpr(10)] int iterations)
```

#### [`[GpuReadOnly]`](GpuReadOnlyAttribute.md)

Emits the standard `const` keyword in C++. The variable remains in the C++ method signature and occupies fast, read-only constant memory caches on the GPU. It is ideal for protecting state and guiding general compiler optimizations.

**C# Source:**

```csharp
public static unsafe void ProcessData(float* data, [GpuReadOnly] float scaleFactor)
```

**Emitted CUDA C++:**

```cpp
__global__ void ProcessData(float* data, const float scaleFactor)
```

## See also

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### BCL API > Gpu Class

- [Gpu Class](../gpu_class/00_index.md)
- [Gpu.Constant Method (G#)](../gpu_class/Constant.md)

### BCL API > Memory

- [Memory Management](../memory/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
