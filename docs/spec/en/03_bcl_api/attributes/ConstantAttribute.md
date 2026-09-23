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

