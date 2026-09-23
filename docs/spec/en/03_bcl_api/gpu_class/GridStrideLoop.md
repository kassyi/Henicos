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

| Name                                                                | Description                                |
| ------------------------------------------------------------------- | ------------------------------------------ |
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
