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
// Execution Context: G# Kernel (GPU)
[GpuDeviceFunction]
public static float ComputeDelta(float a, float b)
{
    return MathF.Abs(a - b);
}
```

## Remarks

Methods marked with this attribute are mapped to `__device__` functions in CUDA C++. They can only be called by `[GpuKernel]` methods or other `[GpuDeviceFunction]` methods.

## See also

### Related Attributes
- [Attributes Index](00_index.md)
- [GpuKernelAttribute](GpuKernelAttribute.md)
- [ConstantAttribute](ConstantAttribute.md)

### Language & Architecture
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)
- [5. Limitations and Diagnostics](../../01_language/05_limitations.md)
- [1. AOT Transpiler Architecture](../../02_architecture/01_aot_transpiler.md)

### BCL & Math APIs
- [Gpu BCL API Overview](../00_index.md)
- [Standard Math Primitives Index](../math_primitives/00_index.md)

### Guides
- [Advanced Custom Math Kernels Guide](../../04_guides/08_advanced_custom_math_kernels.md)
- [Antipatterns and Best Practices Guide](../../04_guides/06_antipatterns_and_best_practices.md)
