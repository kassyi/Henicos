---
title: "ConstantAttribute Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Marks a kernel argument as an Ahead-Of-Time (AOT) preprocessor constant, enabling aggressive hardware-level optimizations."
---

## ConstantAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a kernel argument as an Ahead-Of-Time (AOT) preprocessor constant, enabling aggressive hardware-level optimizations.

```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public sealed class ConstantAttribute : Attribute
```
## Examples

### Loop Unrolling with Constant

```csharp
[GpuKernel]
public static unsafe void MultiplyKernel(int* data, [Constant] int iterations)
{
    // ...
}
```

## Remarks

The parameter is removed from the generated C++ function signature entirely. Instead, the AOT transpiler injects a `#define` macro.

## See also

### Related Attributes
- [Attributes Index](00_index.md)
- [GpuKernelAttribute](GpuKernelAttribute.md)
- [GpuDeviceFunctionAttribute](GpuDeviceFunctionAttribute.md)

### Language & Type System
- [3. Type System (Constants)](../../01_language/03_type_system.md#26-constants-const)
- [1. AOT Transpiler Architecture](../../02_architecture/01_aot_transpiler.md)

### BCL & Memory Architecture
- [Gpu BCL API Overview](../00_overview.md)
- [Gpu.Constant<T>](../gpu_class/Constant.md)
- [Memory Architecture Index](../memory/00_index.md)

### Guides
- [Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
