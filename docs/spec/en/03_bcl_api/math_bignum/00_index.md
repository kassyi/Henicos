---
title: "Standard Math Library (Stdlib.Math)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Overview of the Kassyi.GSharp.Stdlib.Math namespace, providing arbitrary-precision arithmetic and high-performance algorithms for the GPU."
---

# Standard Math Library (Stdlib.Math)

The `Kassyi.GSharp.Stdlib.Math` namespace provides high-performance, arbitrary-precision arithmetic (`GpuBigInteger`, `GpuBigFloat`) and advanced mathematical algorithms designed for the GPU.

> [!NOTE]
> **Dual Architecture: Host-Orchestrated vs GPU-Direct**
>
> The `Stdlib.Math` library offers two distinct execution models:
>
> 1. **Host-Orchestrated (Dynamic Precision)**: Types like the non-generic `GpuBigInteger` rely on `GpuContext` memory pools and coordinate thousands of limbs for million-bit arithmetic. These are **Host-Side Only** and cannot be used inside `[GpuKernel]`.
> 2. **GPU-Direct (Fixed Precision)**: Types like `GpuBigFloat<TSize>` and `GpuUIntBigInteger<TSize>` use C# 12 `[InlineArray]` (e.g., `LimbBuffer8`) to allocate fixed-size buffers directly on the stack/registers. These are **100% Zero-Allocation** and execute natively _inside_ `[GpuKernel]`.

## Namespaces and Components

This module is split into several logical sub-components:

1. **[GpuContext](gpu_context/GpuContext.md)**
   The core memory pool and execution state manager required for all lazy-evaluated expressions and multi-precision operations.

2. **[Arbitrary-Precision Integers (GpuBigInt)](gpu_big_int_and_uint/00_index.md)**
   Provides `GpuUIntBigInteger` (unsigned) and `GpuBigInteger` (signed) types capable of handling numbers with millions of bits.

3. **[Arbitrary-Precision Floats (GpuBigFloat)](gpu_big_float/00_index.md)**
   Provides `GpuBigFloat` for floating-point calculations requiring extreme precision.

## Memory Management and Zero-Allocation

G# enforces a strict Zero-Allocation rule inside GPU kernels. The `Stdlib.Math` library satisfies this in two different ways depending on the model:

- **GPU-Direct Types (`<TSize>`)**:
  Uses C# 12 `[InlineArray]` (such as `LimbBuffer8`) to map array-like structures directly to contiguous registers or local memory. Instantiating these (e.g. `new LimbBuffer8()`) does **not** allocate memory on the heap. They are completely GC-free value types (`struct`), allowing you to use object-oriented syntax without violating zero-allocation rules.

    ```csharp
    // Because it is a struct with [InlineArray], it acts as a fixed-size
    // value type buffer. No heap allocation (GC) occurs when instantiated.
    [InlineArray(8)]
    public struct LimbBuffer8
    {
        private uint _element0;
    }

    [GpuKernel]
    public static void Example()
    {
        // 100% Zero-Allocation: This allocates 256 bits (8 x 32-bit)
        // directly on the stack (or as registers in PTX), NOT on the heap.
        var buffer = new LimbBuffer8();
        buffer[0] = 3;
        var floatVal = new GpuBigFloat<LimbBuffer8>(/*...*/);
    }
    ```

- **Host-Orchestrated Types**:
  Relies entirely on pre-allocated device memory managed by a `GpuContext`. Temporary buffers are checked out from the context's memory pool and recycled instantly, eliminating `cudaMalloc` overhead during execution.

> [!IMPORTANT]
> **Fixed-Size Limits vs. True Arbitrary Precision**
>
> While the GPU-Direct `<TSize>` variants are incredibly fast and memory-efficient (zero-allocation), they are fundamentally **fixed-size** (e.g., 256-bit or 512-bit). If a calculation exceeds the maximum capacity of `TSize`, **the higher-order bits are silently truncated**.
>
> The _true_ power of GSharp's BigNum implementation lies in the **Host-Orchestrated** model. By using `GpuContext`, you unlock truly arbitrary precision (capable of handling numbers with millions of bits) that seamlessly coordinates distributed computations across thousands of GPU threads without truncation.

## See also

### Architecture

- [2. Compiler and Runtime Architecture](../../02_architecture/00_index.md)
- [4. BigNum (BigInt/BigFloat) Internal Architecture](../../02_architecture/04_bignum_internals.md)

### BCL API

- [BCL API and Hardware Intrinsics Reference](../00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [4. BigNum Expression Trees Guide](../../04_guides/04_bignum_expression_trees_guide.md)
