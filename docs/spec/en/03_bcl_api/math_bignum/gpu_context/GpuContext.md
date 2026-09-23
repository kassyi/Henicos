---
title: "GpuContext Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for managing GPU memory pools and execution context for massive numbers."
---

## GpuContext Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

The `GpuContext` manages the lifecycle and memory pooling for arbitrary-precision integers on the GPU. 

Because `cudaMalloc` operations are extremely slow and cause synchronization bottlenecks, GSharp enforces a **Zero-Allocation** paradigm within its math kernels. The `GpuContext` pre-allocates a massive memory pool on the GPU during instantiation. AST evaluators and algorithms dynamically "checkout" and "return" scratch buffers from this pool in nanoseconds.

## Constructors

| Name | Description |
| --- | --- |
| [GpuContext(int)](#gpucontext-int) | Initializes a new context with a specific word capacity. |

## GpuContext(int)

Initializes a new context memory pool capable of handling `maxWords` per number.

```csharp
public GpuContext(int maxWords);
```

#### Parameters

`maxWords` _int_<br>
The maximum size (in 32-bit words) of the numbers this context can process. For example, `maxWords: 1024` allows processing numbers up to $1024 \times 32 = 32,768$ bits.

## Properties

| Name | Description |
| --- | --- |
| `MaxWords` | Gets the maximum number of words allowed per allocation. |

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Destroys the memory pool and frees GPU VRAM. |

## Dispose()

Frees the underlying CUDA memory block.

```csharp
public void Dispose();
```

## Examples

### Batch Processing with Context Reuse

```csharp
// Execution Context: Host (CPU)

// Pre-allocate a 4096-bit pool once
using var context = new GpuContext(maxWords: 128); 

for (int batch = 0; batch < 1000; batch++)
{
    // The GpuContext pool is reused automatically.
    // Temporary variables are checked out from the pool and returned upon Dispose().
    using var a = GpuUIntBigInteger.FromBigInteger(context, ...);
    using var b = GpuUIntBigInteger.FromBigInteger(context, ...);
    
    var ast = new GpuBigInteger(a) + new GpuBigInteger(b);
    using var result = ast.Node.Evaluate(context, null);
    
    Console.WriteLine($"Batch {batch} complete.");
}
```

## Remarks

The `GpuContext` must be explicitly passed into all memory allocation calls (`FromBigInteger`, `FromByteArray`) and into AST evaluations (`Node.Evaluate()`). It implements `IDisposable` to guarantee clean shutdown of the CUDA context.

## See also



### BCL API > Attributes

- [Attributes](../../attributes/00_index.md)
- [Kassyi.GSharp.Core: TakesOwnershipAttribute class](../../attributes/TakesOwnershipAttribute.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../../../04_guides/02_memory_safety_patterns.md)

