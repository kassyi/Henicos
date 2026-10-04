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
  Architecture and compilation strategy for arbitrary-precision arithmetic (`GpuBigInt`, `GpuBigFloat`) and lazy expression evaluation.

## See also



### Language Specification

- [1. Language Specification](../01_language/00_index.md)

### BCL API

- [BCL API and Hardware Intrinsics Reference](../03_bcl_api/00_index.md)

