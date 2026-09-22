---
title: "Kassyi.GSharp.Core: Advanced Mapping Attributes"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Advanced attributes used primarily by the standard library to map C# constructs to native CUDA C++ representations."
---

The `Kassyi.GSharp.Core` package includes several advanced internal mapping attributes. These are used primarily by the G# Base Class Library (BCL) and the standard library to map C# constructs to native CUDA C++ representations during transpilation.

## Attributes

### `[CudaIntrinsic]`
Maps a specific C# method directly to a CUDA C++ function call.

### `[CudaIntrinsicPattern(string pattern)]`
A Class/Struct-level attribute used to map all members of a type to a C++ pattern. 
The `{0}` token represents the lowercase name of the invoked member. 
- *Example*: `[CudaIntrinsicPattern("threadIdx.{0}")]` maps `ThreadIndex.X` to `threadIdx.x`.

### `[BclIntrinsicMap]`
An assembly-level attribute used by the transpiler to map specific BCL method signatures directly to GPU hardware intrinsics.
- *Example*: Maps `System.Numerics.BitOperations.PopCount` to `__popc`.

### `[GpuOperatorMacro]`
Maps C# operator overloads (like `+` or `*`) to specific GPU device functions via `TargetType` and `TargetMethod`.

### `[SkipSnapshotConsistency]`
Used exclusively in testing to bypass snapshot consistency verification for specific, volatile kernels.

## See also

- [Gpu BCL API Overview](../00_overview.md)
