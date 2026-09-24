---
title: "ScopedGpuMemory Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A disposable stack-only ref struct representing a scoped allocation of GPU global device memory."
---

## ScopedGpuMemory Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

A disposable stack-only ref struct representing a scoped allocation of GPU global device memory.

```csharp
public ref struct ScopedGpuMemory(IGpuExecutionProvider provider, int numWords)
```

## Properties

| Name                | Description                                                  |
| ------------------- | ------------------------------------------------------------ |
| [Pointer](#pointer) | Gets the raw native GPU device pointer (<c>CUdeviceptr</c>). |

## Pointer

Gets the raw native GPU device pointer (<c>CUdeviceptr</c>).

```csharp
public IntPtr Pointer { get; }
```

## Methods

| Name                  | Description                            |
| --------------------- | -------------------------------------- |
| [Dispose()](#dispose) | Frees the allocated GPU memory buffer. |

## Dispose()

Frees the allocated GPU memory buffer.

```csharp
public void Dispose();
```

## Remarks

Ensures deterministic deallocation of temporary GPU memory buffers upon exiting the using scope.

## See also

### BCL API > Math (BigNum)

- [GPU Big Integer and Unsigned Integer](../00_index.md)
