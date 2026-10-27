---
title: "Arbitrary-Precision Floats (GpuBigFloat)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Overview of the GpuBigFloat module for arbitrary-precision floating-point arithmetic."
---

## Arbitrary-Precision Floats (GpuBigFloat)

The `GpuBigFloat` module provides high-performance, arbitrary-precision floating-point arithmetic on the GPU. It is designed for applications requiring precision far beyond standard IEEE 754 `float` or `double` (e.g., multi-million-digit $\pi$ computation, fractal generation, and high-precision scientific simulations).

## Core Types

| Type | Description |
| --- | --- |
| [`GpuBigFloat`](GpuBigFloat.md) | Represents an arbitrary-precision signed floating-point number wrapping a [`GpuBigUFloat`](GpuBigUFloat.md) magnitude and a sign bit. |
| [`GpuBigUFloat`](GpuBigUFloat.md) | Represents the unsigned magnitude ($\text{Significand} \times 2^{\text{Exponent}}$) of an arbitrary-precision floating-point number. |
| [`GpuBigFloatArray`](GpuBigFloatArray.md) | A contiguous array of `GpuBigFloat` values in device memory; storing a wider value rounds it toward zero to the element width. |
| [`GpuBigFloatRoundingMode`](GpuBigFloatRoundingMode.md) | Specifies the IEEE 754 compliant rounding mode. |
| [`IGpuBigFloatArithmetic`](IGpuBigFloatArithmetic.md) | Defines the contract for arbitrary-precision floating-point arithmetic operations executed across GPU and CPU backends. |
| [`GSharpKernelFloatProvider`](GSharpKernelFloatProvider.md) | Execution provider dispatching `IGpuBigFloatArithmetic` operations to native CUDA kernels, GSharp AOT kernels, or a fallback strategy. |
| [`CSharpCpuBigFloatArithmetic`](CSharpCpuBigFloatArithmetic.md) | Stateless host (CPU) implementation of `IGpuBigFloatArithmetic`; its `Exp`, `Log` and `Pow` return the same bits as the GPU. |
| [`GpuBigFloatTranscendentalOptions`](GpuBigFloatTranscendentalOptions.md) | Tuning parameters of `Exp`, `Log` and `Pow` per workspace (`GpuWorkspace.Transcendental`). |
| [`GpuBigFloatKernels`](../gpu_big_int_and_uint/kernels/GpuBigFloatKernels.md) | Native G# (CUDA) kernels for radix-aligned floating-point addition and significand normalization. |
| [`FloatExpressions`](../gpu_big_int_and_uint/expressions/FloatExpressions.md) | Zero-allocation generic binary float expressions (`BinaryExpr<T, TOp>`) and `GpuBigFloatExpressionVisitor`. |
| [`Operators (+, -, *)`](Operators.md) | Operator overloads for `GpuBigFloat` and `GpuBigUFloat`. |

## Rounding

The result of an operation has $p = 32n$ bits for the $n$ words of its result significand. `Add`, `Multiply`, `Divide`, `Reciprocal`, `Sqrt`, `Rsqrt`, `Exp`, `Log` and `Pow` of [`IGpuBigFloatArithmetic`](IGpuBigFloatArithmetic.md), and [`GpuBigFloatArray.SetElement`](GpuBigFloatArray.md) of a wider value, return the exact value rounded toward zero to $p$ bits, so a value that fits in $p$ bits is returned exactly. The result depends only on the values of the operands, not on their word counts or leading zero words, and the GPU and the CPU provider return the same bits. When the exponent of the result does not fit in `int`, the operation throws `OverflowException` instead of wrapping around; an intermediate exponent outside `int` is not an error when the result fits. `DivideByUInt32` is an integer division of the significand, not a rounding to $p$ bits. No operation takes a rounding mode: `Round`, which would take a [`GpuBigFloatRoundingMode`](GpuBigFloatRoundingMode.md), is not implemented.

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
