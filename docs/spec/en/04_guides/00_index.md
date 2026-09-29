---
title: "4. Practical Guides and Best Practices"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "In-depth development guides, optimization patterns, and architectural best practices for G#."
---

# 4. Practical Guides and Best Practices

Writing high-performance GPU code requires shifting from a CPU-centric mental model to massively parallel SIMT execution. These practical guides walk through optimization techniques, memory safety, hardware synchronization, and development workflows.

## Guides & Topics

- **[4.1 Warp-Level Programming Guide](01_warp_programming_guide.md)**
  Master warp shuffles, active thread masking, and divergence-free SIMT algorithms without shared memory overhead.
- **[4.2 Memory Safety Patterns](02_memory_safety_patterns.md)**
  Zero-leak architecture, strict disposable ownership with `[TakesOwnership]`, and borrowed resource conventions.
- **[4.3 Exception Handling Guide](03_exception_handling_guide.md)**
  Understanding compile-time diagnostics vs. device-side fault isolation and `GpuOnlyException`.
- **[4.4 Bignum Expression Trees Guide](04_bignum_expression_trees_guide.md)**
  Building and executing arbitrary-precision mathematical expression trees on the GPU with lazy evaluation.
- **[4.5 Precision and Performance Guide](05_precision_and_performance_guide.md)**
  FastMath approximations, memory coalescing, loop unrolling, and floating-point accuracy trade-offs.
- **[4.6 Antipatterns and Best Practices Guide](06_antipatterns_and_best_practices.md)**
  The top 6 traps C# developers encounter on GPUs (warp divergence, uncoalesced memory, dynamic `new`, spin-locks, PCIe chatter, `Console.WriteLine`) and how to fix them.
- **[4.7 Stdlib Math and Lazy Evaluation Guide](07_stdlib_math_and_lazy_evaluation.md)**
  Signed vs. unsigned algorithm separation, modular arithmetic, and AST expression orchestration.
- **[4.8 Advanced Custom Math Kernels Guide](08_advanced_custom_math_kernels.md)**
  Authoring complex custom mathematical operations combining primitives, PTX intrinsics, and helper functions.
- **[4.9 2D Grid Mapping and Image Processing Guide](09_2d_grid_mapping_and_image_processing.md)**
  Mapping 2D/3D matrix and image coordinates to GPU thread grids with boundary guards.
- **[4.10 AI Agent Coding Guidelines](10_ai_agent_coding_guidelines.md)**
  Best practices and common hallucination guards for AI coding assistants writing G# code.
- **[4.11 Deep Struct Marshaling and Argument Hell Elimination Guide](11_deep_struct_marshaling_guide.md)**
  A comprehensive guide to eliminating Argument Hell using deep struct marshaling, AOT Contexts, named arguments, and default parameters.

## See also




### Getting Started

- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)

### Language Specification

- [1. Language Specification](../01_language/00_index.md)

### Architecture

- [2. Compiler and Runtime Architecture](../02_architecture/00_index.md)

### BCL API

- [BCL API and Hardware Intrinsics Reference](../03_bcl_api/00_index.md)

