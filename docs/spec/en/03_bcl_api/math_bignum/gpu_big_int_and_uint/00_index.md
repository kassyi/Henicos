---
title: "Arbitrary-Precision Integers (GpuBigInt)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Overview of the GpuBigInt module, detailing the separation of unsigned memory buffers and signed wrappers."
---

# Arbitrary-Precision Integers (GpuBigInt)

The `GpuBigInt` module provides high-performance multi-precision arithmetic on the GPU. It is designed to handle extremely large integers (e.g., millions of bits) for applications such as cryptography, prime number generation, and advanced mathematics.

> [!WARNING]
> **Host-Side Only API**  
> The classes and operator overloads in this module are designed exclusively for host-side orchestration. You cannot instantiate these types or call their operators inside a `[GpuKernel]`.

## Architectural Design: Unsigned vs. Signed

To maximize GPU performance and avoid branching (Warp Divergence), the GSharp runtime separates the concepts of memory allocation and sign evaluation.

1. **[GpuUIntBigInteger](GpuUIntBigInteger.md)**:
   The unsigned base type. This class manages the actual `IntPtr` to the GPU device memory. It contains no concept of positive or negative. All low-level arithmetic kernels (ripple-carry addition, multiplication) operate strictly on unsigned memory buffers to ensure deterministic execution paths.

2. **[GpuBigInteger](GpuBigInteger.md)**:
   The signed wrapper struct. This lightweight struct simply pairs a `GpuUIntBigInteger` reference with an `IsNegative` boolean flag. When you perform signed arithmetic (e.g., adding a negative number), the host-side operator overloads evaluate the `IsNegative` flags and dispatch the appropriate underlying unsigned kernels (e.g., dispatching a subtraction kernel instead of an addition kernel).

This separation ensures that GPU kernels remain purely mathematical and branchless, leaving the orchestration and sign logic to the Host CPU.

### Core API

- **[GpuUIntBigInteger.md](GpuUIntBigInteger.md)**: The underlying unsigned memory buffer on the GPU.
- **[GpuBigInteger.md](GpuBigInteger.md)**: The signed wrapper struct for host-side orchestration.
- **[Operators.md](Operators.md)**: Overloads for `+`, `-`, `*`, `/`, and bitwise operators.
- **[BigIntegerExtensions.md](BigIntegerExtensions.md)**: Host-side math extensions for `System.Numerics.BigInteger`.

### Advanced Algorithms & Transforms

GSharp includes a vast suite of advanced algorithms designed to leverage the GPU for massive computations (e.g., Cryptography, Pi Calculation, Fractal rendering).

- **[Gcd.md](algorithms/Gcd.md)**: Greatest Common Divisor (Lehmer's GCD).
- **[ModPow.md](algorithms/ModPow.md)**: Modular Exponentiation (RSA cryptography).
- **[Ntt.md](algorithms/Ntt.md)**: Number Theoretic Transform for $O(N \log N)$ convolution of massive numbers.
- **[Fermat.md](algorithms/Fermat.md)**: Fermat's Little Theorem orchestrator for primality testing and factorization.
- **[Hypergeometric.md](algorithms/Hypergeometric.md)**: Binary Splitting primitives for Hypergeometric Series (Chudnovsky Pi calculation).
- **[Matrix2x2.md](algorithms/Matrix2x2.md)**: 2x2 Matrix arithmetic for $O(\log N)$ linear recurrences (e.g., Fibonacci).
- **[DivRem.md](algorithms/DivRem.md)**: Simultaneous extraction of Quotient and Remainder.

### Input / Output

- **[IO_GpuRadixConverter.md](IO_GpuRadixConverter.md)**: Divide-and-Conquer Base-10 string conversion for printing millions of digits in $O(N \log^2 N)$ time.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [Arbitrary-Precision Floats (GpuBigFloat)](../gpu_big_float/00_index.md)
