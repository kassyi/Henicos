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

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Transpiler Inspection

Example.Run();

public class Example
{
    [GpuKernel]
    public static unsafe void VectorAdd(float* a, float* b, float* c)
    {
        // ...
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(VectorAdd));
        var hasAttr = method.GetCustomAttribute<GpuKernelAttribute>() != null;

        Console.WriteLine($"Method '{method.Name}' has [GpuKernel]: {hasAttr}");
    }
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



### Getting Started

- [0. Getting Started with G# (GSharp)](../../00_getting_started/00_index.md)
- [3. The "Hello GPU" Tutorial](../../00_getting_started/03_hello_gpu.md)

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../02_architecture/00_index.md)
- [2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)](../../02_architecture/02_runtime_loader.md)

### BCL API > Execution Context

- [Execution Context](../execution_context/00_index.md)

### BCL API > Gpu Class

- [Gpu Class](../gpu_class/00_index.md)
- [Gpu.GridStrideLoop Method (G#)](../gpu_class/GridStrideLoop.md)

