---
title: "Standard Math Library (Stdlib.Math)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Overview of the Kassyi.GSharp.Stdlib.Math namespace, providing arbitrary-precision arithmetic and high-performance algorithms for the GPU."
---

# Standard Math Library (Stdlib.Math)

The `Kassyi.GSharp.Stdlib.Math` namespace provides high-performance, arbitrary-precision arithmetic (`GpuBigInteger`, `GpuBigFloat`) and advanced mathematical algorithms designed for the GPU.

> [!WARNING]
> **Host-Side Only API**
> 
> The vast majority of the advanced types in this namespace (such as `GpuBigInteger`, `GpuBigFloat`, and their operator overloads) are designed **exclusively for host-side orchestration**. 
> You **cannot** use these types or call their `+`, `-`, `*`, `/` operators directly from *inside* a `[GpuKernel]` method.
> 
> All arbitrary-precision arithmetic operations involve orchestrating multiple low-level CUDA kernels and managing workspace memory pools. Therefore, you must dispatch and evaluate them from the CPU (Host) using the `GpuContext`. For a detailed explanation of *why* this is restricted and what alternatives exist, see the **[Advanced Math & Lazy Evaluation Guide](../../04_guides/07_stdlib_math_and_lazy_evaluation.md)**.

## Namespaces and Components

This module is split into several logical sub-components:

1. **[GpuContext](gpu_context/GpuContext.md)**
   The core memory pool and execution state manager required for all lazy-evaluated expressions and multi-precision operations.
   
2. **[Arbitrary-Precision Integers (GpuBigInt)](gpu_big_int_and_uint/00_overview.md)**
   Provides `GpuUIntBigInteger` (unsigned) and `GpuBigInteger` (signed) types capable of handling numbers with millions of bits.

3. **[Arbitrary-Precision Floats (GpuBigFloat)](gpu_big_float/00_overview.md)**
   Provides `GpuBigFloat` for floating-point calculations requiring extreme precision.

## Shared Memory and Allocations

Because GSharp enforces a strict Zero-Allocation rule inside GPU kernels, the `Stdlib.Math` library relies entirely on pre-allocated device memory managed by a `GpuContext`. All temporary buffers required for arithmetic carry-propagation or intermediate multiplication steps are checked out from the context's memory pool and recycled instantly, eliminating `cudaMalloc` overhead during execution.
