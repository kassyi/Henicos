---
title: "Gpu.GridStrideLoop Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Runs a body once for each index, spread over every thread of the launch."
---

## Gpu.GridStrideLoop Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Runs a body once for each index of a range, spread over every thread of the launch (a grid-stride loop).

## Overloads

| Name | Description |
| --- | --- |
| [GridStrideLoop(int, Action\<int\>)](#gridstrideloopint-actionint) | Generates a standard CUDA Grid-Stride Loop over the specified bound. |
| [GridStrideLoop\<TContext\>(ref TContext, int, Gpu.GridStrideAction\<TContext\>)](#gridstridelooptcontextref-tcontext-int-gpugridstrideactiontcontext) | Generates a standard CUDA Grid-Stride Loop with context state propagation. |

## GridStrideLoop(int, Action\<int\>)

Runs `body` once for each index of `[0, maxBounds)`, spread over every thread of the launch.

```csharp
public static void GridStrideLoop(int maxBounds, Action<int> body);
```

#### Parameters

`maxBounds` _int_<br>
The total element count to process. It is evaluated once; a negative count runs no iteration.

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

`Gpu.GridStrideLoop` runs the body once for each index of `[0, maxBounds)`, spread over every thread of the launch. Each thread starts at its linear rank in the whole launch and steps by the number of threads in the launch. The rank counts x fastest, then y, then z, and the blocks in the same order, as cooperative groups `grid.thread_rank()` and `grid.num_threads()` do. Every index is therefore visited exactly once for every launch shape: 1D, 2D or 3D blocks and grids, and launches of 2^31 threads or more.

`maxBounds` is evaluated once, before the first iteration, as any C# argument is; a body that changes what it was read from does not change the number of iterations. A negative `maxBounds` runs no iteration.

The call expands into a flat loop without any delegate allocation. The CUDA build, the native CPU build and the managed path all use the same three helpers:

```cpp
// Execution Context: Generated C++ (GPU)
{
unsigned int __gsharp_gsl_n_0 = __gsharp_grid_stride_count(count);
for(unsigned int __gsharp_gsl_0 = __gsharp_grid_stride_start(); __gsharp_gsl_0 < __gsharp_gsl_n_0; __gsharp_gsl_0 += __gsharp_grid_stride_step())
{
int i = (int)__gsharp_gsl_0;
// body...
}
}
```

The start and the step stop at 2^31, which no `int` index reaches, so the unsigned index never wraps around. In a 1D launch, the start is `blockIdx.x * blockDim.x + threadIdx.x` and the step is `gridDim.x * blockDim.x`, the classic grid-stride loop.

A loop that gives each row of a 2D launch its own `Gpu.GridStrideLoop` (reading the row from `BlockIndex.Y` or `ThreadIndex.Y`) spreads the indices of every row over all the threads of the launch, so each row only sees part of them. Write such a per-row loop over `ThreadIndex.X` yourself:

```csharp
// Execution Context: G# Kernel (GPU), launched with gridDimY: rows
int row = BlockIndex.Y;
for (int c = BlockIndex.X * BlockDimension.X + ThreadIndex.X; c < cols; c += GridDimension.X * BlockDimension.X)
{
    output[row * cols + c] = row;
}
```

The body is written into the surrounding code, so two things that are legal in an ordinary C# lambda are rejected with [GSH0003](../diagnostics/GSH0003.md):

- A `return` statement. In C# it would end one iteration, but in the generated code it would end the kernel. Instead of leaving early with `return`, wrap the rest of the body in `if (...) { ... }`. A `return` inside a nested lambda or local function of the body belongs to that function and is allowed.
- An assignment to the loop variable (`i = 0`, `i += 1`, `i++`, `--i`, or passing it as a `ref` / `out` argument), or a reference to it (an `in` argument, or `&i`). The generated code uses the loop's own index, which the loop advances and which has no address of its own. A local variable declared in the body, and the context parameter of the `ref TContext` overload, can be assigned.

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
