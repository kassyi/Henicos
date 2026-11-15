---
title: "GpuReadOnlyAttribute Class (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Marks a kernel parameter, field, or property as read-only for the whole launch; an array keeps const elements, and writing it is error GSH0118."
---

## GpuReadOnlyAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a kernel parameter, field, or property as read-only for the whole launch. It is emitted with the `const` qualifier in the generated C++ source (an array, span or other array-like value with `const` elements).

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

On an array, the attribute is a promise: **no thread writes the array while the kernel runs, through any alias.** G# reports a write it can see as [GSH0118](../diagnostics/GSH0118.md): an element assignment, `++`, a `ref` element, `Fill` and the like, also through a span or array converted from the array and through a method that writes its parameter. A conversion that only reads (`uint[] x = h.Fixed;`, `ReadOnlySpan<uint> s = r;`) is allowed.

G# cannot see the same host array passed to a `[GpuReadOnly]` parameter and to a writable parameter of one launch. A write there breaks the promise, and the result is undefined.

The attribute does not place anything in constant memory and adds no `__restrict__` today; it changes the type of the C++ declaration. The promise lets a later version read the elements through the read-only data path of the GPU.

### Comparison with [GpuConstexpr]

While both attributes optimize constant values, their underlying mechanics differ:

- **`[GpuReadOnly]` is like an `in` parameter or `readonly` field:** The parameter physically exists in memory and is passed as an argument, but the compiler qualifies it with `const`.
- **[[GpuConstexpr]](ConstantAttribute.md) marks a compile-time constant:** It marks a kernel parameter whose value is meant to be fixed at compile time. Its candidate values are not used yet: the parameter is still passed as an ordinary runtime argument. A later release compiles one `constexpr` specialization per candidate value.

#### [GpuReadOnly]

Emits the standard `const` keyword in C++. The variable remains in the C++ method signature. It protects state: a write to a `[GpuReadOnly]` array is rejected as GSH0118.

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
