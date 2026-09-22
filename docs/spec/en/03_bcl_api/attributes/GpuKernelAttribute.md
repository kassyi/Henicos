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

### Related Attributes
- [Attributes Index](00_index.md)
- [GpuDeviceFunctionAttribute](GpuDeviceFunctionAttribute.md)
- [ParameterInteropAttributes](ParameterInteropAttributes.md)
- [ConstantAttribute](ConstantAttribute.md)

### Language & Execution Model
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)
- [5. Limitations and Diagnostics](../../01_language/05_limitations.md)
- [2. Runtime Kernel Loader](../../02_architecture/02_runtime_loader.md)

### BCL & GPU Intrinsics
- [Gpu BCL API Overview](../00_index.md)
- [Execution Context Overview](../execution_context/00_index.md)
- [Gpu.GridStrideLoop](../gpu_class/GridStrideLoop.md)

### Guides & Tutorials
- [3. The "Hello GPU" Tutorial](../../00_getting_started/03_hello_gpu.md)
- [2D Grid Mapping and Image Processing Guide](../../04_guides/09_2d_grid_mapping_and_image_processing.md)
- [Antipatterns and Best Practices Guide](../../04_guides/06_antipatterns_and_best_practices.md)
