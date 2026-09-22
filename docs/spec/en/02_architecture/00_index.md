---
title: "2. Compiler and Runtime Architecture"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Architectural overview of the G# Roslyn AOT Transpiler, Runtime Loader, and execution pipeline."
---

# 2. Compiler and Runtime Architecture

G# seamlessly bridges high-level C# Roslyn syntax trees with bare-metal NVIDIA CUDA C++ execution through a two-tier architecture: compile-time AOT transpilation and a zero-overhead runtime loader.

This section details the inner workings of the transpilation engine, binary verification, orchestration wrappers, and bignum internals.

## Chapters

- **[2.1 AOT Transpiler Architecture](01_aot_transpiler.md)**
  Roslyn syntax interception, AST walking, C++ code generation, and multi-backend dialect abstraction (`ITranspilerDialect`).
- **[2.2 Runtime Kernel Loader](02_runtime_loader.md)**
  Module loading, binary integrity hashing, CUDA driver API invocation, and managed wrapper generation.
- **[2.3 Comprehensive End-to-End Pipeline Example](03_comprehensive_example.md)**
  A complete walkthrough from C# source code to generated CUDA `.cu`, native `.fatbin`/`.dll`, and host invocation.
- **[2.4 Bignum Transpiler Internals](04_bignum_internals.md)**
  Architecture and compilation strategy for arbitrary-precision arithmetic (`GpuBigInteger`, `GpuBigFloat`) and lazy expression evaluation.

## See also

### Getting Started & Language
- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)
- [1. Language Specification Overview](../01_language/00_index.md)

### BCL API Reference
- [Gpu BCL API Overview](../03_bcl_api/00_index.md)
- [Arbitrary-Precision Math (Bignum) Overview](../03_bcl_api/math_bignum/00_index.md)

### Guides
- [Memory Safety Patterns Guide](../04_guides/02_memory_safety_patterns.md)
- [Bignum Expression Trees Guide](../04_guides/04_bignum_expression_trees_guide.md)
