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

### Core GPU Intrinsics
* [01. Intrinsics and Attributes](attributes/00_index.md) — The fundamental `[GpuKernel]` and hardware mapping attributes.
* [02. `Gpu` Class Reference](gpu_class/00_index.md) — Warp shuffles, atomic operations, barriers (`Gpu.SyncThreads`), and loop unrolling macros.
* [03. Execution Indices](execution_context/00_index.md) — Thread and Block coordinate access (`ThreadIndex.X`, `BlockIndex.Y`).
* [04. Shared Memory](memory/00_index.md) — High-throughput SM block-local memory allocations (`SharedMemory<T>`).
* [05. Vector Types](math/VectorTypes.md) — Strongly-typed, aligned vector structs (`Float4`, `Int2`) for maximized memory bandwidth.
* [06. Math and Fast Math](math/00_index.md) — Hardware-native approximations (`FastMath.Exp`, `FastMath.Log`) and standard BCL math.

### Big Number Arithmetic (GPU-Native)
* [07. `GpuBigInt`](03_math_gpubigint.md) — Zero-allocation, arbitrary-precision integer arithmetic optimized for CUDA.
* [08. `GpuBigFloat`](02_math_gpubigfloat.md) — Zero-allocation, arbitrary-precision floating-point arithmetic.

### Standard BCL and Diagnostics
* [09. Diagnostics and Exceptions](04_diagnostics_and_exceptions.md) — Exception handling constraints, `GpuOnlyException`, and the complete list of Transpiler Diagnostic Codes (`GSHxxxx`).
* [10. Standard BCL Mappings](05_bcl_mappings.md) — Standard .NET methods mapped to PTX (`System.Math`, `System.Numerics.BitOperations`, `System.Threading.Interlocked`).
* [11. Diagnostic Reference Catalog](diagnostics/00_index.md) — Detailed pages with causes, solutions, and Before/After examples for each diagnostic code (`GSH0001`–`GSH0029`, `RS0030`).
