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
| [GridStrideLoop(int, Action<int>)](#gridstrideloop-int-action-int-) | Executes a standard CUDA grid-stride loop. |

## GridStrideLoop(int, Action<int>)

Executes a standard CUDA grid-stride loop.

```csharp
[GpuKernel]
public static void GridStrideLoop(int count, Action<int> body);
```

#### Parameters

`count` <i>int</i><br>
The total number of elements to process.

`body` <i>Action&lt;int&gt;</i><br>
A delegate or lambda expressing the loop body. The integer parameter represents the global index.

## Examples

### Grid Stride Loop

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static unsafe void VectorAdd(int count, float* a, float* b, float* c)
{
    Gpu.GridStrideLoop(count, i => 
    {
        c[i] = a[i] + b[i];
    });
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

## See also

### Kernel Flow Control
- [SingleThread](SingleThread.md)
- [HintUnroll](HintUnroll.md)

### Execution Context
- [Execution Context Overview](../execution_context/00_index.md)
- [ThreadIndex](../execution_context/ThreadIndex.md)
- [BlockDimension](../execution_context/BlockDimension.md)
- [GridDimension](../execution_context/GridDimension.md)

### Language & Architecture
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)
- [1. AOT Transpiler Architecture](../../02_architecture/01_aot_transpiler.md)
- [Gpu BCL API Overview](../00_overview.md)

### Tutorials & Guides
- [3. The "Hello GPU" Tutorial](../../00_getting_started/03_hello_gpu.md)
- [2D Grid Mapping and Image Processing Guide](../../04_guides/09_2d_grid_mapping_and_image_processing.md)
- [Antipatterns and Best Practices Guide](../../04_guides/06_antipatterns_and_best_practices.md)
