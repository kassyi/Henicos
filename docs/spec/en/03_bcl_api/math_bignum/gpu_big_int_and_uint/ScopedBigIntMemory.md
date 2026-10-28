---
title: "ScopedBigIntMemory Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides a deterministic, stack-scoped RAII wrapper for temporary device memory allocations managed by an IGpuMemoryManager."
---

## ScopedBigIntMemory Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides a deterministic, stack-scoped RAII wrapper for temporary device memory allocations.

```csharp
public ref struct ScopedBigIntMemory(GpuBigIntMemoryHandle handle)
```

## Constructors

| Name | Description |
| --- | --- |
| [ScopedBigIntMemory(GpuBigIntMemoryHandle)](#scopedbigintmemorygpubigintmemoryhandle) | Initializes a new stack-scoped memory wrapper for the specified handle. |

## ScopedBigIntMemory(GpuBigIntMemoryHandle)

Initializes a new stack-scoped memory wrapper for the specified handle.

```csharp
public ScopedBigIntMemory(GpuBigIntMemoryHandle handle);
```

#### Parameters

`handle` _GpuBigIntMemoryHandle_<br>
The device memory handle bound to this scope. An owning handle (`OwnsBuffer`) is freed by `Dispose` through the memory manager that allocated it; a borrowed handle is only used.

## Properties

| Name | Description |
| --- | --- |
| [Handle](#handle) | Gets the underlying device memory handle. |

## Handle

Gets the underlying device memory handle.

```csharp
public GpuBigIntMemoryHandle Handle { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Releases an owned allocation back to the `IGpuMemoryManager` that allocated it. |

## Dispose()

Releases an owned allocation back to the `IGpuMemoryManager` that allocated it; a borrowed handle is left alone. A second `Dispose` does nothing.

```csharp
public void Dispose();
```

## Operators

| Name | Description |
| --- | --- |
| [Implicit(in ScopedBigIntMemory to GpuBigIntMemoryHandle)](#implicitin-scopedbigintmemory-to-gpubigintmemoryhandle) | Defines an implicit conversion from a scoped allocation to its raw `GpuBigIntMemoryHandle`. |

## Implicit(in ScopedBigIntMemory to GpuBigIntMemoryHandle)

Defines an implicit conversion from a scoped allocation to its raw `GpuBigIntMemoryHandle`.

```csharp
public static implicit operator GpuBigIntMemoryHandle(in ScopedBigIntMemory memory);
```

## Remarks

Because `ScopedBigIntMemory` is a `ref struct`, it is strictly stack-allocated and cannot escape the calling frame, preventing accidental memory leaks or managed heap allocations during high-throughput GPU kernel staging.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(maxWords: 1024, engine: GpuExecutionEngine.GSharpAot);
using ScopedBigIntMemory scopedMem = workspace.MemoryManager.AllocateTemp(numWords: 16);

// Implicit conversion to GpuBigIntMemoryHandle
GpuBigIntMemoryHandle handle = scopedMem;
Console.WriteLine($"Scoped handle NumWords: {handle.NumWords}");
```

## See also

### BCL API > Math (BigNum)

- [IGpuMemoryManager Interface](IGpuMemoryManager.md)
- [IGpuBigIntArithmetic Interface](IGpuBigIntArithmetic.md)
- [GpuWorkspace Class](../gpu_workspace/GpuWorkspace.md)
