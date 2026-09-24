---
title: "GpuContext Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a GPU execution context managing memory allocation pools, device workspaces, and backend providers for large integer computations."
---

## GpuContext Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents a GPU execution context managing memory allocation pools, device workspaces, and backend providers for large integer computations.

```csharp
public class GpuContext : IDisposable
```



## Constructors

| Name | Description |
| --- | --- |
| [GpuContext(int, GpuExecutionEngine)](#gpucontextint-gpuexecutionengine) | Initializes a new instance of the `GpuContext` class with the specified limb capacity and engine backend. |

## GpuContext(int, GpuExecutionEngine)

Initializes a new instance of the `GpuContext` class with the specified limb capacity and engine backend.

```csharp
public GpuContext(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot);
```

#### Parameters

`maxWords` _int_<br>
The maximum word count for BigInteger operations.

`engine` _GpuExecutionEngine_<br>
The GPU execution backend engine to utilize.

#### Exceptions

`InvalidOperationException`<br>
Thrown when CUDA context or native workspace allocation fails.

## Properties

| Name | Description |
| --- | --- |
| [Engine](#engine) | Gets the active GPU execution engine type. |
| [MaxWords](#maxwords) | Gets the maximum number of 32-bit limbs (words) supported in this context's fixed allocations. |
| [Provider](#provider) | Gets the active GPU arithmetic execution provider implementation. |
| [WorkspacePointer](#workspacepointer) | Gets the raw native device pointer to the allocated workspace scratchpad memory (used by legacy native backend). |

## Engine

Gets the active GPU execution engine type.

```csharp
public GpuExecutionEngine Engine { get; }
```

## MaxWords

Gets the maximum number of 32-bit limbs (words) supported in this context's fixed allocations.

```csharp
public int MaxWords { get; }
```

## Provider

Gets the active GPU arithmetic execution provider implementation.

```csharp
public IGpuExecutionProvider Provider { get; }
```

## WorkspacePointer

Gets the raw native device pointer to the allocated workspace scratchpad memory (used by legacy native backend).

```csharp
public IntPtr WorkspacePointer { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Releases all device memory resources, temporary pooled buffers, and workspace handles held by this context. |
| [Dispose(bool)](#disposebool) | Releases unmanaged and, optionally, managed resources. |

## Dispose()

Releases all device memory resources, temporary pooled buffers, and workspace handles held by this context.

```csharp
public void Dispose();
```

## Dispose(bool)

Releases unmanaged and, optionally, managed resources.

```csharp
protected virtual void Dispose(bool disposing);
```

#### Parameters

`disposing` _bool_<br>
`true` to release both managed and unmanaged resources; `false` to release only unmanaged resources.

## Remarks

Instantiating `GpuContext` allocates workspace memory on the active CUDA device. Ensure instances are properly disposed to prevent device memory exhaustion.

**Thread Safety:** Instance members are thread-safe for allocation pooling.

## Examples

### Allocating a Workspace and Native Compilation Engine

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math;

// 1. Create a GpuContext with a specific capacity
using var context = new GpuContext(maxWords: 1024, engine: GpuExecutionEngine.GSharpAot);

// 2. Output properties to verify initialization
// Outputs: MaxWords: 1024, Engine: GSharpAot
Console.WriteLine($"MaxWords: {context.MaxWords}");
Console.WriteLine($"Engine: {context.Engine}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuContext](../../../03_bcl_api/math_bignum/gpu_context/GpuContext.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
