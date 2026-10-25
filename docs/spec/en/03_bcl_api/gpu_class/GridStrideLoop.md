---
title: "Gpu.GridStrideLoop Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Executes a standard CUDA grid-stride loop."
---

## Gpu.GridStrideLoop Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Executes a standard CUDA grid-stride loop.

## Overloads

| Name | Description |
| --- | --- |
| [GridStrideLoop(int, Action\<int\>)](#gridstrideloopint-actionint) | Generates a standard CUDA Grid-Stride Loop over the specified bound. |
| [GridStrideLoop\<TContext\>(ref TContext, int, Gpu.GridStrideAction\<TContext\>)](#gridstridelooptcontextref-tcontext-int-gpugridstrideactiontcontext) | Generates a standard CUDA Grid-Stride Loop with context state propagation. |

## GridStrideLoop(int, Action\<int\>)

Generates a standard CUDA Grid-Stride Loop over the specified bound across all threads and blocks.

```csharp
public static void GridStrideLoop(int maxBounds, Action<int> body);
```

#### Parameters

`maxBounds` _int_<br>
The total element count to process.

`body` _Action\<int\>_<br>
The loop body delegate receiving the current thread stride index.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU outside managed execution.

## GridStrideLoop\<TContext\>(ref TContext, int, Gpu.GridStrideAction\<TContext\>)

Generates a standard CUDA Grid-Stride Loop with context state propagation.

```csharp
public delegate void GridStrideAction<TContext>(ref TContext ctx, int index);

public static void GridStrideLoop<TContext>(
    ref TContext ctx,
    int maxBounds,
    Gpu.GridStrideAction<TContext> body);
```

#### Type Parameters

`TContext`<br>
The state structure type passed by reference across thread loop strides.

#### Parameters

`ctx` _TContext_<br>
The execution context passed by reference.

`maxBounds` _int_<br>
The total element count to process.

`body` _Gpu.GridStrideAction\<TContext\>_<br>
The loop body delegate receiving the context and current stride index.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU outside managed execution.

## Examples

### Grid Stride Loop

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU)
try {
    int totalElements = 1024;
    // Automates standard CUDA Grid-Stride Loop
    Gpu.GridStrideLoop(totalElements, i => 
    {
        Console.WriteLine($"Processing element {i}");
    });
} catch (GpuOnlyException) {
    Console.WriteLine("Gpu.GridStrideLoop can only be executed on the GPU.");
}
```

## Remarks

Expands into a flat loop without any delegate allocation:

```cpp
// Execution Context: Generated C++ (GPU)
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < count; i += blockDim.x * gridDim.x)
{
    // body...
}
```

Under managed execution, each thread starts at `blockDim.x * blockIdx.x + threadIdx.x` and steps by `blockDim.x * gridDim.x`, as in the loop above.

## See also

### Getting Started

- [0. Getting Started with G# (GSharp)](../../00_getting_started/00_index.md)
- [3. The "Hello GPU" Tutorial](../../00_getting_started/03_hello_gpu.md)

### BCL API > Execution Context

- [Execution Context](../execution_context/00_index.md)
- [BlockDimension Struct (Kassyi.GSharp.Core)](../execution_context/BlockDimension.md)
- [ThreadIndex Struct (Kassyi.GSharp.Core)](../execution_context/ThreadIndex.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [2D/3D Grid Mapping and Image Processing Patterns](../../04_guides/09_2d_grid_mapping_and_image_processing.md)
