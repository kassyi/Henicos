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

- [Gpu BCL API Overview](../00_overview.md)
