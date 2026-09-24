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

## Group 1: Core G# Framework

The Core Framework provides the essential building blocks for writing, synchronizing, and launching GPU kernels.

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

### 5. Math and Numerics (Primitives)
Standard C# math functions are transpiled to their highly-optimized CUDA equivalents.

> [!NOTE]
> **Device & Host Execution**
> Because these are standard C# BCL methods (like `System.Math.Sin`), they can naturally be executed on the Host (CPU). GSharp's transpiler extends this capability, allowing them to also be executed seamlessly inside GPU kernels (Device).

* **[System.Math and System.MathF](math_primitives/00_index.md)**
* **[System.Numerics.BitOperations](math_primitives/00_index.md)**
* **[FastMath](math_primitives/00_index.md)**: Hardware-accelerated approximate instructions (`__expf`, `__logf`).
* **[Built-in Vector Types](math_primitives/VectorTypes.md)**: Strongly-typed, aligned vector structs (`Float4`, `Int2`) for maximized memory bandwidth.

### 6. Standard BCL and Diagnostics
* **[Standard BCL Mappings](02_bcl_mappings.md)** — Standard .NET methods mapped to PTX (`System.Math`, `System.Numerics.BitOperations`, `System.Threading.Interlocked`).
* **[Diagnostics and Exceptions](01_diagnostics_and_exceptions.md)** — Exception handling constraints, `GpuOnlyException`, and the complete list of Transpiler Diagnostic Codes (`GSHxxxx`).
* **[Diagnostic Reference Catalog](diagnostics/00_index.md)** — Detailed pages with causes, solutions, and Before/After examples for each diagnostic code (`GSH0001`–`GSH0029`, `RS0030`).

### 7. Utilities
* **[CudaEnvironment](CudaEnvironment.md)**: Discover and resolve local NVIDIA CUDA Toolkit installation directories.

---

## Group 2: Domain Specific Libraries (`Stdlib.Math` / BigNum)

The `Stdlib.Math` library is a highly specialized suite built on top of the Core Framework, designed for arbitrary-precision arithmetic and advanced algorithmic orchestration on the GPU.

> [!WARNING]
> **Host-Side Only API**
> Unlike standard C# math primitives, BigNum operations orchestrate multiple GPU kernels and allocate device memory. Therefore, these APIs are strictly **Host-Only** and *cannot* be called from within a `[GpuKernel]`.

* **[BigNum Overview](math_bignum/00_index.md)**
* **[GPU Big Integer (Signed & Unsigned)](math_bignum/gpu_big_int_and_uint/00_index.md)**
* **[GPU Big Float](math_bignum/gpu_big_float/00_index.md)**
* **[GPU Execution Contexts](math_bignum/gpu_context/GpuContext.md)**

## See also



### Language Specification

- [1. Language Specification](../01_language/00_index.md)
- [1. Foundation and Domain](../01_language/01_introduction.md)

### Architecture

- [2. Compiler and Runtime Architecture](../02_architecture/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../04_guides/00_index.md)

