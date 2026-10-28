---
title: "Arbitrary-Precision Integers (GpuBigInt)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Overview of the GpuBigInt module, detailing the separation of unsigned memory buffers and signed wrappers."
---

## Arbitrary-Precision Integers (GpuBigInt)

The `GpuBigInt` module provides high-performance multi-precision arithmetic on the GPU. It is designed to handle extremely large integers (e.g., millions of bits) for applications such as cryptography, prime number generation, and advanced mathematics.

> [!WARNING]
> **Host-Side Only API**  
> The classes and operator overloads in this module are designed exclusively for host-side orchestration. You cannot instantiate these types or call their operators inside a `[GpuKernel]`.

## Architectural Design: Unsigned vs. Signed

To maximize GPU performance and avoid branching (Warp Divergence), the GSharp runtime separates the concepts of memory allocation and sign evaluation.

1. **[GpuBigUInt](GpuBigUInt.md)**:
   The unsigned base type. This class manages the actual `IntPtr` to the GPU device memory. It contains no concept of positive or negative. All low-level arithmetic kernels (ripple-carry addition, multiplication) operate strictly on unsigned memory buffers to ensure deterministic execution paths.

2. **[GpuBigInt](GpuBigInt.md)**:
   The signed wrapper struct. This lightweight struct simply pairs a `GpuBigUInt` reference with an `IsNegative` boolean flag. When you perform signed arithmetic (e.g., adding a negative number), the host-side operator overloads evaluate the `IsNegative` flags and dispatch the appropriate underlying unsigned kernels (e.g., dispatching a subtraction kernel instead of an addition kernel).

This separation ensures that GPU kernels remain purely mathematical and branchless, leaving the orchestration and sign logic to the Host CPU.

### Core API

- **[GpuBigUInt.md](GpuBigUInt.md)**: The underlying unsigned memory buffer on the GPU.
- **[GpuBigInt.md](GpuBigInt.md)**: The signed wrapper struct for host-side orchestration.
- **[GpuBigUIntArray.md](GpuBigUIntArray.md)**: Contiguous batch array of unsigned multi-precision integers.
- **[GpuBigIntArray.md](GpuBigIntArray.md)**: Contiguous batch array of signed multi-precision integers.
- **[GpuBigUIntView.md](GpuBigUIntView.md)** / **[GpuBigIntView.md](GpuBigIntView.md)** / **[GpuBigUIntArrayView.md](GpuBigUIntArrayView.md)** / **[GpuBigIntArrayView.md](GpuBigIntArrayView.md)**: Borrowed, stack-only views (`ref struct`, like `Span<T>`) of values and array ranges; the operations take them and owned values convert implicitly.
- **[GpuBigUIntGeneric.md](GpuBigUIntGeneric.md)**: Fixed-size unsigned multi-precision integer struct (`GpuBigUInt<TSize>`) for value-type/kernel execution.
- **[GpuUInt128.md](GpuUInt128.md)** / **[GpuUInt256.md](GpuUInt256.md)** / **[GpuUInt512.md](GpuUInt512.md)**: Fixed-width 128/256/512-bit unsigned integer structs for GPU device and host execution.
- **[GpuBigUIntBuilder.md](GpuBigUIntBuilder.md)**: Factory builder facilitating C# 12 Collection Expression construction for fixed-size `GpuBigUInt<TSize>`.
- **[IGpuNumber.md](IGpuNumber.md)**: Marker interface (`IGpuNumber<TSelf>`) and `[GenerateGpuNumberOperators]` attribute for source-generated GPU number operators.
- **[IGpuBigIntArithmetic.md](IGpuBigIntArithmetic.md)**: Handle-based GPU device memory contract for arbitrary-precision integer arithmetic.
- **[IGpuMemoryManager.md](IGpuMemoryManager.md)**: Memory allocation and scoped temporary buffer lifetime contract (`ScopedBigIntMemory`).
- **[ScopedBigIntMemory.md](ScopedBigIntMemory.md)**: Stack-scoped `ref struct` RAII wrapper for temporary device memory allocations.
- **[Operators.md](Operators.md)**: Overloads for `+`, `-`, `*`, `/`, and bitwise operators.
- **[BigIntegerExtensions.md](BigIntegerExtensions.md)**: Host-side math extensions (`BigIntExtensions.Sqrt`) for `System.Numerics.BigInteger`.

### Execution Providers & Handlers

- **[IGpuExecutionProvider.md](providers/IGpuExecutionProvider.md)**: Aggregated contract (`IGpuMemoryProvider`, `IGpuBasicMathProvider`, `IGpuAdvancedMathProvider`, `IGpuGraphProvider`) for GPU execution backends.
- **[GSharpKernelProvider.md](providers/GSharpKernelProvider.md)**: Primary execution provider using AOT-compiled PTX kernels and CPU fallback orchestration.
- **[CSharpCpuBigIntProvider.md](providers/CSharpCpuBigIntProvider.md)**: Pure C# CPU execution provider for hardware-free fallback and diagnostic verification.
- **[ScopedGpuMemory.md](providers/ScopedGpuMemory.md)**: Disposable stack-only `ref struct` for scoped raw device memory pointers.
- **[KernelMemoryHandler.md](providers/KernelMemoryHandler.md)** / **[KernelBasicMathHandler.md](providers/KernelBasicMathHandler.md)** / **[KernelAdvancedMathHandler.md](providers/KernelAdvancedMathHandler.md)** / **[KernelGraphHandler.md](providers/KernelGraphHandler.md)**: Specialized modular handlers backing `GSharpKernelProvider`.
- **[StreamCompactionOrchestrator.md](providers/StreamCompactionOrchestrator.md)**: Multi-block recursive Blelloch exclusive prefix sum orchestrator.

### Lazy Expressions, CUDA Graphs & Vector LINQ

- **[GpuBigIntExpr.md](expressions/GpuBigIntExpr.md)**: AST builder and lazy expression evaluation on GPU.
- **[GpuBigUIntExpr.md](expressions/GpuBigUIntExpr.md)**: Unsigned AST builder and bitwise/arithmetic expression evaluation on GPU.
- **[IGpuBigIntExpression.md](expressions/IGpuBigIntExpression.md)**: Interface for lazily evaluated signed GPU BigInteger AST nodes.
- **[IGpuBigUIntExpression.md](expressions/IGpuBigUIntExpression.md)**: Interface for lazily evaluated unsigned GPU BigInteger AST nodes using `BigIntExpressionVisitor`.
- **[GpuBigUIntExpressionExtensions.md](expressions/GpuBigUIntExpressionExtensions.md)**: Extension method (`Evaluate`) for evaluating `IGpuBigUIntExpression` AST nodes.
- **[BigIntExpressionVisitor.md](expressions/BigIntExpressionVisitor.md)**: Stack-scoped `ref struct` visitor that evaluates `IGpuBigUIntExpression` AST nodes.
- **[GpuBigIntExecutionContext.md](GpuBigIntExecutionContext.md)**: Stack-scoped `readonly ref struct` encapsulating the workspace, memory manager, arithmetic provider, and CUDA stream.
- **[FloatExpressions.md](expressions/FloatExpressions.md)**: Zero-allocation generic binary float expressions (`BinaryExpr<T, TOp>`) and `GpuBigFloatExpressionVisitor`.
- **[GpuBigIntExecutableGraph.md](expressions/GpuBigIntExecutableGraph.md)**: Compiled, reusable CUDA Graph execution engine.
- **[GpuBigIntVectorLinqExtensions.md](expressions/GpuBigIntVectorLinqExtensions.md)**: Batched LINQ extensions and parallel sum reductions.
- **[GpuBigIntStackLinqExtensions.md](expressions/GpuBigIntStackLinqExtensions.md)**: Zero-allocation stack-based parallel LINQ operations.
- **[IGpuBigIntVectorExpression.md](expressions/IGpuBigIntVectorExpression.md)**: Interface for vectorized deferred GPU expressions.
- **[IGpuBoolVectorExpression.md](expressions/IGpuBoolVectorExpression.md)**: Interface for deferred GPU vector boolean predicate expressions.
- **[BoolVectorExpressions.md](expressions/BoolVectorExpressions.md)**: Deferred scalar boolean expression (`IGpuBoolExpr`) and `VectorCompareOperator` enum.

### Advanced Algorithms & Transforms

GSharp includes a vast suite of advanced algorithms designed to leverage the GPU for massive computations (e.g., Cryptography, Pi Calculation, Fractal rendering).

- **[Gcd.md](algorithms/Gcd.md)**: Greatest Common Divisor (Lehmer's GCD).
- **[ModPow.md](algorithms/ModPow.md)**: Modular Exponentiation (RSA cryptography).
- **[Ntt.md](algorithms/Ntt.md)**: Number Theoretic Transform for $O(N \log N)$ convolution of massive numbers.
- **[FermatAlgorithm.md](algorithms/FermatAlgorithm.md)**: Fermat probable-prime test of `N = 2^(2E+1) - 2^E + 11` on the GPU (2E + 1 NTT squarings with a dedicated fold), with the Gerbicz-Li check, checkpoints and a non-blocking `RunAsync` (formerly `FermatRunner`, see [Fermat.md](algorithms/Fermat.md)).
- **[PrimeSearchAlgorithm.md](algorithms/PrimeSearchAlgorithm.md)**: High-performance probable prime generation using trial division and Fermat PRP testing.
- **[ChudnovskyAlgorithm.md](algorithms/ChudnovskyAlgorithm.md)**: Hybrid CPU/GPU Chudnovsky binary splitting pipeline for multi-million-digit $\pi$ computation.
- **[BbpAlgorithm.md](algorithms/BbpAlgorithm.md)**: Bailey-Borwein-Plouffe (BBP) spigot algorithm for direct hexadecimal $\pi$ digit extraction.
- **[PiVerifier.md](algorithms/PiVerifier.md)**: Verifies trailing hexadecimal $\pi$ digits computed on the GPU against `BbpAlgorithm`.
- **[Hypergeometric.md](algorithms/Hypergeometric.md)**: Binary Splitting primitives (`HypergeometricTerms`) for Hypergeometric Series (Chudnovsky Pi calculation).
- **[Matrix2x2.md](algorithms/Matrix2x2.md)**: 2x2 Matrix arithmetic for $O(\log N)$ linear recurrences (e.g., Fibonacci).
- **[DivRem.md](algorithms/DivRem.md)**: Simultaneous extraction of Quotient and Remainder.

### Input / Output

- **[IO_GpuRadixConverter.md](IO_GpuRadixConverter.md)**: Divide-and-Conquer Base-10 string conversion for printing millions of digits in $O(N \log^2 N)$ time.
- **[IO_HeadTailTrackerWriter.md](IO_HeadTailTrackerWriter.md)**: Memory-efficient `TextWriter` retaining only the leading and trailing $N$ characters of massive output streams.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [Arbitrary-Precision Floats (GpuBigFloat)](../gpu_big_float/00_index.md)
