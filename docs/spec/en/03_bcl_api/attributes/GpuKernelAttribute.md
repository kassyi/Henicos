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
public class GpuKernelAttribute : Attribute
```

## Properties

| Name | Description |
| --- | --- |
| [AllowOversizedSharedMemory](#allowoversizedsharedmemory) | Gets or sets a value indicating whether a launch of this kernel may use an oversized shared memory configuration of the GPU. |
| [Cooperative](#cooperative) | Gets or sets a value indicating whether this kernel is launched as a cooperative kernel (`cuLaunchCooperativeKernel` / CUDA Cooperative Groups). |

## AllowOversizedSharedMemory

Gets or sets a value indicating whether a launch of this kernel may use an oversized shared memory configuration of the GPU.

```csharp
public bool AllowOversizedSharedMemory { get; set; }
```

GPUs of compute capability 10.7 can give one block up to 327 KB of shared memory (static plus dynamic) instead of 227 KB, but only for a kernel that asks for it: while such a launch runs, the kernel has only 8 KB of L1 cache. Setting the property is that request. The amount is still the `sharedMemBytes` of each launch:

- On CUDA, a launch whose `sharedMemBytes` fits the normal limit of the GPU makes the same driver calls with or without the property. A larger launch on a GPU that has an oversized configuration sets the kernel's shared memory mode (`CU_FUNC_ATTRIBUTE_SHARED_MEMORY_MODE`) once, before its first such launch. On a GPU without one, the property changes nothing.
- On the CPU paths (native, managed and `KernelExecution.CSharp`), a launch is checked against the smallest limit among the GPUs that the fatbin of `GSharpTargetArchitectures` may run on. For a kernel with the property, a GPU that has an oversized configuration counts with its oversized limit. The property therefore raises the CPU limit only when every target GPU has an oversized configuration:

| `GSharpTargetArchitectures` | GPUs it may run on | Without the property | With the property |
| :- | :- | :- | :- |
| default (`compute_86,sm_86;compute_86,compute_86`) | 8.6 to 12.1 | 101376 (sm_86) | 101376 (sm_86) |
| `compute_86,sm_86` | 8.6, 8.9 | 101376 (sm_86) | 101376 (sm_86), and warning [GSH0116](../diagnostics/GSH0116.md) |
| `compute_100,sm_100` | 10.0, 10.3, 10.7 | 232448 (sm_100) | 232448 (sm_100) |
| `compute_107,sm_107` or `sm_107a` | 10.7 | 232448 (sm_107) | 334848 (sm_107) |

A launch above the limit throws `ArgumentOutOfRangeException` (`ParamName` `sharedMemBytes`) on every path, before any thread runs. When the property would have let it run, the message says so.

> [!NOTE]
> The CUDA side of this property has not been run on a GPU of compute capability 10.7. It follows the CUDA 13.4 headers and the CUDA Programming Guide; the decision of when to set the mode is covered by tests without a GPU, and a GPU without an oversized configuration (sm_86) is tested to behave as without the property.

## Cooperative

Gets or sets a value indicating whether this kernel is launched as a cooperative kernel (`cuLaunchCooperativeKernel` / CUDA Cooperative Groups).

```csharp
public bool Cooperative { get; set; }
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

### Where a Kernel May Be Declared

A kernel runs only through the `Launch{Kernel}` method that G# generates into its class, so `[GpuKernel]` is allowed only on an ordinary `static` method of a class (a `static` class, a record class, or a class nested in a type that is not generic). The class and every type around it must be `partial` ([GSH0036](../diagnostics/GSH0036.md)). G# reports [GSH0111](../diagnostics/GSH0111.md) and generates nothing for a kernel that is:

- an operator, a conversion operator, an accessor, a lambda or a local function;
- an instance method, or a member of a struct, an interface, a C# 14 extension block or a `file` type;
- a generic method, or a member of a generic type;
- the entry point, a `[ModuleInitializer]` or `[UnmanagedCallersOnly]` method, or a method that implements an interface member;
- an extension method that C# calls by its name (`GetEnumerator`, `GetAsyncEnumerator`, `Deconstruct`, `GetAwaiter`, `GetPinnableReference`, `Add`, `Dispose`, `DisposeAsync`), or a method that a `[CollectionBuilder]` attribute names.

Put the body in a `[GpuDeviceFunction]` method and call it from a kernel declared as above.

### Allowed Parameter Types (Signature Whitelist)

The `[GpuKernel]` method signature strictly permits only the following types:

| Category | Allowed | Prohibited |
| :--- | :--- | :--- |
| **Primitives** | Value types (`int`, `float`, `bool`, etc.) | Reference types (`class`, `string`, interfaces, delegates, `object`), and arrays of them ([GSH0112](../diagnostics/GSH0112.md)) |
| **Structs** | Blittable `struct` (passed by value, `ref`, `in` / `ref readonly`, or `out`), or structs with 1D array fields (`T[]`) | Structs containing non-array reference types |
| **Arrays** | 1D Managed Arrays (`T[]`) | Multi-dimensional Arrays (`T[,]`) |
| **Spans / Buffers** | `Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ReadOnlyMemory<T>`, `ArraySegment<T>`, `ImmutableArray<T>` | N/A |
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
