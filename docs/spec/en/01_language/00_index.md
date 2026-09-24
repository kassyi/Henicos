---
title: "1. Language Specification"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Language specification, execution semantics, and type constraints for G# (GSharp)."
---

# 1. Language Specification

G# is an Embedded Compute Dialect (eDSL) powered by standard C# syntax, targeting NVIDIA GPU hardware via Roslyn Source Generators and native CUDA transpilation.

This section defines the core language syntax, type system constraints, execution semantics, and compile-time diagnostics enforced by the G# Transpiler.

## Chapters

- **[1.1 Language Introduction](01_introduction.md)**
  Overview of G#, the AOT transpilation pipeline, and dialect architecture.
- **[1.2 Lexical Structure and Syntax](02_lexical_and_syntax.md)**
  Supported C# syntax constructs, statements, control flow, and local function rules.
- **[1.3 Type System](03_type_system.md)**
  Zero-allocation type constraints, blittable structs, vector primitives, and constant handling.
- **[1.4 Execution and Concurrency Model](04_execution_model.md)**
  SIMT execution model, Grids, Blocks, Warps, memory hierarchies, and hardware synchronization.
- **[1.5 Limitations and Diagnostics](05_limitations.md)**
  Compile-time static determinism, prohibited C# features (classes, LINQ, strings), and compiler diagnostics (`GSHxxxx`).

## See also



### Getting Started

- [0. Getting Started with G# (GSharp)](../00_getting_started/00_index.md)

### Architecture

- [2. Compiler and Runtime Architecture](../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../02_architecture/01_aot_transpiler.md)

### BCL API

- [BCL API and Hardware Intrinsics Reference](../03_bcl_api/00_index.md)

