---
title: "BCL API and Hardware Intrinsics Reference"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "The `03_bcl_api` section of the G# specification provides a comprehensive, MSDN-style reference for all public APIs, types, and hardware intrinsics..."
---

# BCL API and Hardware Intrinsics Reference

The `03_bcl_api` section of the G# specification provides a comprehensive, MSDN-style reference for all public APIs, types, and hardware intrinsics exposed by the `Kassyi.GSharp.Core` namespace, as well as the officially supported mappings to the standard .NET Base Class Library (BCL).

Because G# kernels execute on GPUs—architectures that lack garbage collectors, runtime type metadata, and dynamic dispatch—these APIs are highly specialized. They map directly to native CUDA C++ / PTX instructions at compile time via the AOT Transpiler.

## Table of Contents

### 1. Core Attributes
Specialized C# attributes that govern compilation, entry point semantics, and JIT optimizations.
* **[GpuKernelAttribute](attributes/GpuKernelAttribute.md)**: Marks the entry point for the G# transpilation process (`__global__`).
* **[GpuDeviceFunctionAttribute](attributes/GpuDeviceFunctionAttribute.md)**: Marks a helper method intended for execution solely on the GPU (`__device__`).
* **[ConstantAttribute](attributes/ConstantAttribute.md)**: Marks a kernel argument as an Ahead-Of-Time (AOT) preprocessor constant.
* **[ParameterInteropAttributes](attributes/ParameterInteropAttributes.md)**: Optimizes PCIe memory transfers for array parameters (`InAttribute`, `OutAttribute`).
* **[TakesOwnershipAttribute](attributes/TakesOwnershipAttribute.md)**: Enforces ownership transfer of disposable GPU resources.
* **[AdvancedMappingAttributes](attributes/AdvancedMappingAttributes.md)**: Internal attributes mapping C# constructs to native CUDA C++.

### 2. Execution Context Indices
Provides access to CUDA's built-in execution context variables.
* **[ThreadIndex](execution_context/ThreadIndex.md)** (`threadIdx`)
* **[BlockIndex](execution_context/BlockIndex.md)** (`blockIdx`)
* **[BlockDimension](execution_context/BlockDimension.md)** (`blockDim`)
* **[GridDimension](execution_context/GridDimension.md)** (`gridDim`)

### 3. The `Gpu` Class Intrinsics
Intrinsic static methods and macros providing direct access to GPU hardware features.
* **[Gpu.SyncThreads](gpu_class/SyncThreads.md)**: Barrier synchronization for threads in a block.
* **[Gpu.AtomicAdd](gpu_class/AtomicAdd.md)**: Atomic read-modify-write addition.
* **[Gpu.InlinePtx](gpu_class/InlinePtx.md)**: Injects raw PTX or CUDA C++ strings.
* **[Gpu.GridStrideLoop](gpu_class/GridStrideLoop.md)**: High-level CUDA grid-stride loop macro.
* **[Gpu.Hint.Unroll](gpu_class/HintUnroll.md)**: Zero-allocation compiler marker for loop unrolling.
* **Warp Shuffles**: Direct access to warp-level shuffle operations (**[WarpShuffleDown](gpu_class/WarpShuffleDown.md)**, **[WarpShuffleUp](gpu_class/WarpShuffleUp.md)**, **[WarpShuffleXor](gpu_class/WarpShuffleXor.md)**).

### 4. Memory
* **[SharedMemory<T>](memory/SharedMemory.md)**: Abstracts CUDA's high-throughput `__shared__` block-local memory allocations.

### 5. Math and Numerics
Standard C# math functions are transpiled to their highly-optimized CUDA equivalents.
* **[System.Math and System.MathF](math/00_index.md)**
* **[System.Numerics.BitOperations](math/00_index.md)**
* **[FastMath](math/00_index.md)**: Hardware-accelerated approximate instructions (`__expf`, `__logf`).
* **[Built-in Vector Types](math/VectorTypes.md)**: Strongly-typed, aligned vector structs (`Float4`, `Int2`) for maximized memory bandwidth.

### 6. Big Number Arithmetic (GPU-Native)
* **[`GpuBigInt`](03_math_gpubigint.md)** — Zero-allocation, arbitrary-precision integer arithmetic optimized for CUDA.
* **[`GpuBigFloat`](02_math_gpubigfloat.md)** — Zero-allocation, arbitrary-precision floating-point arithmetic.

### 7. Standard BCL and Diagnostics
* **[Standard BCL Mappings](05_bcl_mappings.md)** — Standard .NET methods mapped to PTX (`System.Math`, `System.Numerics.BitOperations`, `System.Threading.Interlocked`).
* **[Diagnostics and Exceptions](04_diagnostics_and_exceptions.md)** — Exception handling constraints, `GpuOnlyException`, and the complete list of Transpiler Diagnostic Codes (`GSHxxxx`).
* **[Diagnostic Reference Catalog](diagnostics/00_index.md)** — Detailed pages with causes, solutions, and Before/After examples for each diagnostic code (`GSH0001`–`GSH0029`, `RS0030`).
