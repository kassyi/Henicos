---
title: "GpuConstAttribute Class (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Marks a kernel parameter, field, or property to be emitted with the const qualifier in the generated CUDA C++ source."
---

## GpuConstAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a kernel parameter, field, or property to be emitted with the `const` qualifier in the generated CUDA C++ source.

```csharp
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property)]
public sealed class GpuConstAttribute : Attribute
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
    public static unsafe void ProcessData(float* data, [GpuConst] float scaleFactor)
    {
        // On CPU, ThreadIndex.X throws GpuOnlyException.
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(ProcessData));
        var param = method.GetParameters()[1];
        var isGpuConst = param.GetCustomAttribute<GpuConstAttribute>() != null;

        Console.WriteLine($"Parameter '{param.Name}' has [GpuConst] attribute: {isGpuConst}");
    }
}
```

## Remarks

Helps the NVIDIA CUDA compiler (`nvcc`/`NVRTC`) optimize register allocation and place constant values into read-only constant memory caches.

### Comparison with [Constant]

While both attributes optimize constant values, their underlying mechanics differ fundamentally. **If you think of this in C# terms:**

- **`[GpuConst]` is like an `in` parameter or `readonly` field:** The variable physically exists in memory and is passed as a real argument, but the compiler ensures it cannot be modified, allowing it to be placed in a fast, read-only memory cache.
- **[[Constant]](ConstantAttribute.md) is like a `const` literal:** The variable completely disappears at compile-time. The compiler takes the raw value (e.g., `10`) and hardcodes it directly into the instructions everywhere it was used.

#### [GpuConst]

Emits the standard `const` keyword in C++. The variable remains in the C++ method signature and occupies fast, read-only constant memory caches on the GPU. It is ideal for protecting state and guiding general compiler optimizations.

**C# Source:**

```csharp
public static unsafe void ProcessData(float* data, [GpuConst] float scaleFactor)
```

**Emitted CUDA C++:**

```cpp
__global__ void ProcessData(float* data, const float scaleFactor)
```

#### [[Constant]](ConstantAttribute.md)

Acts as a strict Ahead-Of-Time (AOT) preprocessor directive. The transpiler completely removes the parameter from the generated C++ signature and injects a `#define` macro. It is mandatory when a physical variable cannot exist (e.g., compile-time loop unroll bounds).

**C# Source (called with `iterations = 10` on the CPU):**

```csharp
public static unsafe void MultiplyKernel(int* data, [Constant] int iterations)
```

**Emitted CUDA C++:**

```cpp
#define iterations 10
__global__ void MultiplyKernel(int* data)
```

## See also

### BCL API > Attributes

- [Attributes](00_index.md)
- [ConstantAttribute](ConstantAttribute.md)
- [GpuKernelAttribute](GpuKernelAttribute.md)

### BCL API > Gpu Class

- [Gpu.Hint.Unroll](../gpu_class/HintUnroll.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
