---
title: "G# BCL API Overview"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Overview of the G# Core Attributes, Intrinsics, and Base Class Library (BCL) APIs."
---

# G# BCL API Overview

This section provides the API Reference for the core attributes, intrinsic APIs, and built-ins provided by `Kassyi.GSharp.Core`. These constructs provide C# IntelliSense support during development but are dynamically intercepted and replaced by the AOT transpiler with raw CUDA equivalents.

## Core Attributes

G# introduces specialized C# attributes that govern compilation, entry point semantics, and JIT optimizations.

- **[`[GpuKernel]`](./attributes/GpuKernelAttribute.md)**: Marks the entry point for the G# transpilation process (`__global__`).
- **[`[GpuDeviceFunction]`](./attributes/GpuDeviceFunctionAttribute.md)**: Marks a helper method intended for execution solely on the GPU (`__device__`).
- **[`[Constant]`](./attributes/ConstantAttribute.md)**: Marks a kernel argument as an Ahead-Of-Time (AOT) preprocessor constant.
- **[`[In]`, `[Out]`](./attributes/ParameterInteropAttributes.md)**: Optimizes PCIe memory transfers for array parameters.
- **[`[TakesOwnership]`](./attributes/TakesOwnershipAttribute.md)**: Enforces ownership transfer of disposable GPU resources.
- **[Advanced Mapping Attributes](./attributes/AdvancedMappingAttributes.md)**: Internal attributes mapping C# constructs to native CUDA C++.

## Execution Context Indices

Provides access to CUDA's built-in execution context variables.

- **[`ThreadIndex`](./execution_context/ThreadIndex.md)** (`threadIdx`)
- **[`BlockIndex`](./execution_context/BlockIndex.md)** (`blockIdx`)
- **[`BlockDimension`](./execution_context/BlockDimension.md)** (`blockDim`)
- **[`GridDimension`](./execution_context/GridDimension.md)** (`gridDim`)

## The `Gpu` Class Intrinsics

Intrinsic static methods and macros providing direct access to GPU hardware features.

- **[`Gpu.SyncThreads`](./gpu_class/SyncThreads.md)**: Barrier synchronization for threads in a block.
- **[`Gpu.AtomicAdd`](./gpu_class/AtomicAdd.md)**: Atomic read-modify-write addition.
- **[`Gpu.InlinePtx`](./gpu_class/InlinePtx.md)**: Injects raw PTX or CUDA C++ strings.
- **[Macro Methods](./gpu_class/MacroMethods.md)**: High-level macros like `GridStrideLoop` and `SingleThread`.
- **[`Gpu.Hint.Unroll`](./gpu_class/HintUnroll.md)**: Zero-allocation compiler marker for loop unrolling.
- **[Warp Shuffle Intrinsics](./gpu_class/WarpShuffle.md)**: Direct access to warp-level shuffle operations (`__shfl_sync`).

## Memory

- **[`SharedMemory<T>`](./memory/SharedMemory.md)**: Abstracts CUDA's `__shared__` memory space.

## Math and Numerics

Standard C# math functions are transpiled to their highly-optimized CUDA equivalents.

- **[System.Math and System.MathF](./math/SystemMath.md)**
- **[Built-in Vector Types](./math/VectorTypes.md)** (`Vector3`, `Vector4`, etc.)
- **[System.Numerics.BitOperations](./math/BitOperations.md)**
- **[FastMath](./math/FastMath.md)**: Hardware-accelerated approximate instructions (`__expf`, `__logf`).
