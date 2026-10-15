---
title: "GpuBigIntExecutionContext Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Encapsulates the execution state, memory manager, arithmetic provider, and CUDA stream for a GPU BigNum operation."
---

## GpuBigIntExecutionContext Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Encapsulates the execution state, memory manager, arithmetic provider, and CUDA stream for a GPU BigNum operation.

```csharp
public readonly ref struct GpuBigIntExecutionContext
```

Inheritance: `ValueType` $\rightarrow$ `GpuBigIntExecutionContext`

## Remarks

As a `readonly ref struct`, `GpuBigIntExecutionContext` lives solely on the stack and incurs zero garbage collection overhead when orchestrating expression tree evaluation via [`BigIntExpressionVisitor`](expressions/BigIntExpressionVisitor.md).

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigIntExecutionContext(GpuWorkspace, IGpuMemoryManager, IGpuBigIntArithmetic, IntPtr)](#gpubigintexecutioncontextgpuworkspace-igpumemorymanager-igpubigintarithmetic-intptr) | Initializes a new instance of the `GpuBigIntExecutionContext` struct with the specified workspace, memory manager, arithmetic provider, and CUDA stream. |
| [GpuBigIntExecutionContext(GpuWorkspace, IGpuMemoryManager, IntPtr)](#gpubigintexecutioncontextgpuworkspace-igpumemorymanager-intptr) | Initializes a new instance of the `GpuBigIntExecutionContext` struct using the default legacy provider adapter for arithmetic operations. |

## GpuBigIntExecutionContext(GpuWorkspace, IGpuMemoryManager, IGpuBigIntArithmetic, IntPtr)

Initializes a new instance of the `GpuBigIntExecutionContext` struct with the specified workspace, memory manager, arithmetic provider, and CUDA stream.

```csharp
public GpuBigIntExecutionContext(
    GpuWorkspace gpuWorkspace,
    IGpuMemoryManager memoryManager,
    IGpuBigIntArithmetic arithmetic,
    IntPtr stream);
```

#### Parameters

`gpuWorkspace` _[GpuWorkspace](../gpu_workspace/GpuWorkspace.md)_<br>
The GPU workspace managing global configuration and device allocation.

`memoryManager` _`IGpuMemoryManager`_<br>
The memory manager handling device buffers and temporary scratch spaces.

`arithmetic` _`IGpuBigIntArithmetic`_<br>
The arithmetic provider handling low-level kernel launches.

`stream` _IntPtr_<br>
The unmanaged CUDA stream (`cudaStream_t`) handle used for asynchronous operations.

## GpuBigIntExecutionContext(GpuWorkspace, IGpuMemoryManager, IntPtr)

Initializes a new instance of the `GpuBigIntExecutionContext` struct using the default legacy provider adapter for arithmetic operations.

```csharp
public GpuBigIntExecutionContext(
    GpuWorkspace gpuWorkspace,
    IGpuMemoryManager memoryManager,
    IntPtr stream);
```

#### Parameters

`gpuWorkspace` _[GpuWorkspace](../gpu_workspace/GpuWorkspace.md)_<br>
The GPU workspace.

`memoryManager` _`IGpuMemoryManager`_<br>
The memory manager.

`stream` _IntPtr_<br>
The CUDA stream handle.

## Properties

| Name | Description |
| --- | --- |
| [Arithmetic](#arithmetic) | Gets the low-level arithmetic operations provider. |
| [GpuWorkspace](#gpuworkspace) | Gets the parent GPU workspace. |
| [MemoryManager](#memorymanager) | Gets the memory manager responsible for device buffer allocations. |
| [Stream](#stream) | Gets the CUDA stream handle associated with this execution pipeline. |

## Arithmetic

Gets the low-level arithmetic operations provider.

```csharp
public IGpuBigIntArithmetic Arithmetic { get; }
```

## GpuWorkspace

Gets the parent GPU workspace.

```csharp
public GpuWorkspace GpuWorkspace { get; }
```

## MemoryManager

Gets the memory manager responsible for device buffer allocations.

```csharp
public IGpuMemoryManager MemoryManager { get; }
```

## Stream

Gets the CUDA stream handle associated with this execution pipeline.

```csharp
public IntPtr Stream { get; }
```

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

// Create a stack-scoped execution context bound to the workspace and stream
var context = new GpuBigIntExecutionContext(workspace, workspace.MemoryManager, workspace.Stream);

Console.WriteLine($"Context initialized. Stream=0x{context.Stream:X}");
```

## See also

### BCL API > Math Bignum

- [BigIntExpressionVisitor Struct](expressions/BigIntExpressionVisitor.md)
- [GpuWorkspace Class](../gpu_workspace/GpuWorkspace.md)
- [Arbitrary-Precision Integers (GpuBigInt)](00_index.md)
