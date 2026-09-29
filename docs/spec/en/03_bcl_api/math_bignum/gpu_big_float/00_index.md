---
title: "Arbitrary-Precision Floats (GpuBigFloat)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Overview of the GpuBigFloat module for arbitrary-precision floating-point arithmetic."
---

# Arbitrary-Precision Floats (GpuBigFloat)

The `GpuBigFloat` module provides high-performance, arbitrary-precision floating-point arithmetic on the GPU. It is designed for applications requiring precision far beyond standard IEEE 754 `float` or `double` (e.g., fractal generation, precise physics simulations).

Unlike earlier versions that required Host orchestration, the types in this module are purely unmanaged structs (`where TSize : unmanaged`) that execute entirely on the GPU within `[GpuKernel]` functions.

## Core Types

| Type                                                              | Description                                                                                                                            |
| :---------------------------------------------------------------- | :------------------------------------------------------------------------------------------------------------------------------------- |
| [`GpuBigFloat<TSize>`](GpuBigFloat.md)                            | Represents an arbitrary-precision signed floating-point number. Acts as a wrapper around an unsigned magnitude and a sign bit.         |
| [`GpuBigUFloat<TSize>`](GpuBigUFloat.md)                          | Represents the unsigned magnitude of an arbitrary-precision floating-point number, eliminating branching overhead during calculations. |
| [`GpuBigFloatRoundingMode`](GpuBigFloatRoundingMode.md)           | Specifies the IEEE 754 compliant rounding mode.                                                                                        |
| [`Fixed-Precision Wrappers (128/256/512)`](GpuFloatFixedTypes.md) | High-level `GpuFloat128` etc. wrappers for zero-allocation GPU execution without generic arguments.                                    |
| [`Operators (+, -, *, /)`](Operators.md)                          | Operator overloads for `GpuBigFloat`.                                                                                                  |

## Advanced Algorithms

> [!NOTE]
> Dedicated algorithm orchestrators for `GpuBigFloat` (e.g., Newton-Raphson Division, Taylor Series Trigonometry, Pi generation) are currently tracked for future implementation.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
