---
title: "Arbitrary-Precision Floats (GpuBigFloat)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Overview of the GpuBigFloat module for arbitrary-precision floating-point arithmetic."
---

# Arbitrary-Precision Floats (GpuBigFloat)

The `GpuBigFloat` module provides high-performance, arbitrary-precision floating-point arithmetic on the GPU. It is designed for applications requiring precision far beyond standard IEEE 754 `float` or `double` (e.g., fractal generation, precise physics simulations).

> [!WARNING]
> **Host-Side Only API**  
> Just like `GpuBigInteger`, the classes and operator overloads in this module are designed exclusively for host-side orchestration. You cannot instantiate these types or call their operators inside a `[GpuKernel]`. Doing so will result in a transpiler compilation error (e.g., `GSH0001` or `RS0030`).

## Core Types

| Type | Description |
| :--- | :--- |
| [`GpuBigFloat`](GpuBigFloat.md) | Represents an arbitrary-precision floating-point number. Acts as a host-side wrapper orchestrating an unsigned mantissa and a signed exponent. |
| [`Operators (+, -, *, /)`](Operators.md) | Lazy-evaluated arithmetic operators for `GpuBigFloat`. They construct Abstract Syntax Trees (AST) for batch GPU execution. |

## Advanced Algorithms

> [!NOTE]
> Dedicated algorithm orchestrators for `GpuBigFloat` (e.g., Newton-Raphson Division, Taylor Series Trigonometry, Pi generation) are currently tracked for future implementation.

## See also

### BigFloat Types & Operators
- [GpuBigFloat](GpuBigFloat.md)
- [Operators](Operators.md)

### Integer Bignum Types
- [GpuBigInteger and GpuUIntBigInteger Overview](../gpu_big_int_and_uint/00_index.md)
- [GpuContext](../gpu_context/GpuContext.md)

### BCL & Architecture
- [Arbitrary-Precision Math Overview](../00_index.md)
- [Gpu BCL API Overview](../../00_index.md)
- [4. Bignum Transpiler Internals](../../../02_architecture/04_bignum_internals.md)

### Guides
- [Stdlib Math and Lazy Evaluation Guide](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [Precision and Performance Guide](../../../04_guides/05_precision_and_performance_guide.md)
