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

## Dispose()

Releases all device memory resources, temporary pooled buffers, and workspace handles held by this context.

```csharp
public void Dispose();
```

## Remarks

Instantiating `GpuContext` allocates workspace memory on the active CUDA device. Ensure instances are properly disposed to prevent device memory exhaustion.

**Thread Safety:** Instance members are thread-safe for allocation pooling.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
