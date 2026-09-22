---
title: "GpuKernelAttribute Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Marks a C# method as a GPU execution entry point."
---

## GpuKernelAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a C# method as a GPU execution entry point.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public sealed class GpuKernelAttribute : Attribute
```
## Examples

### Usage

```csharp
[GpuKernel]
public static unsafe void VectorAdd(float* a, float* b, float* c)
{
    // ...
}
```

## Remarks

Methods marked with this attribute are mapped to `__global__` functions in CUDA C++. They are invoked from the CPU (Host) using the G# Runtime.

### Allowed Parameter Types (Signature Whitelist)
The `[GpuKernel]` method signature strictly permits only the following types:

| Category | Allowed | Prohibited |
| :--- | :--- | :--- |
| **Primitives** | Value types (`int`, `float`, `bool`, etc.) | Reference types (`class`, `string`) |
| **Structs** | Blittable `struct` (passed by value, `ref`, or `out`) | Structs containing references |
| **Arrays** | 1D Managed Arrays (`T[]`) | Multi-dimensional Arrays (`T[,]`) |
| **Spans** | N/A (Only allowed as local variables inside the kernel) | `Span<T>`, `ReadOnlySpan<T>` |
| **Pointers** | Raw Pointers (`T*` - requires `unsafe` keyword) | N/A |

> [!WARNING]
> If your kernel accepts only 1D arrays (`T[]`), you do **not** need the `unsafe` keyword. The transpiler handles the host-to-device pointer mapping automatically.

## See also

- [Gpu BCL API Overview](../00_overview.md)
