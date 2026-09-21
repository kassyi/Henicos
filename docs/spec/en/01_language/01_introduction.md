# 1. Foundation and Domain

---

## Architectural Motivation & Design Tenets

G# bridges the Microsoft Roslyn compiler platform with the NVIDIA CUDA compilation toolchain (NVCC) to enable high-throughput GPU computing directly from standard C# source code.

Instead of introducing a proprietary external language or standalone runtime, G# leverages Roslyn's semantic analysis to validate and translate C# Abstract Syntax Trees (AST) into optimized CUDA C++ device code. Furthermore, because G# kernel logic adheres strictly to standard C# syntax and semantics, kernels maintain dual-target execution capability: executing directly on the CPU/.NET runtime without a physical GPU for local debugging and CI/CD verification, while transpiling to native CUDA C++ for production GPU workloads.

### Core Technical Tenets

1. **AST-Driven Transpilation (Roslyn)**: All syntactic analysis, semantic validation, and code emission are performed at compile-time via Roslyn Syntax Nodes and Semantic Models.
2. **Zero-Allocation**: Device execution fundamentally prohibits managed heap allocations (`class`, `string`, dynamic allocations). All kernel operations strictly utilize value types (`struct`), stack/register memory, and device buffers.
3. **Static Determinism**: Kernel logic and type resolution must be fully determinable at compile time. Dynamic dispatch, reflection, and runtime LINQ are strictly prohibited.
4. **Ubiquitous Execution (Host-Device Equivalence)**: G# kernels maintain dual-target equivalence: fully transpilable into native CUDA C++ for high-throughput GPU execution, while preserving complete CPU execution compatibility for low-cost, GPU-less testing and deterministic debugging.

---

## 1.1 G# Definition and Scope

This document defines the foundational concepts, scope, and hybrid compilation architecture of the G#　transpiler and runtime framework.

G# is a domain-specific C#-to-CUDA transpilation engine and runtime environment. It enables developers to write GPU compute kernels using standard C# syntax, which are then intercepted, translated into CUDA C++ / PTX, and executed natively on NVIDIA GPUs.

G# is **not** a general-purpose programming language. It is a highly restricted subset of C# designed strictly for Massively Parallel Processor (MPP) architectures.

### 1.1.1 Core Principles

- **Zero-Allocation**: G# execution occurs entirely on the GPU device. Heap allocations (`new Class()`, `string`) are strictly prohibited within the kernel context.
- **Deterministic Execution**: Kernel logic must be statically analyzable at compile time. Dynamic dispatch, reflection, and runtime type inference are unsupported.
- **Native Performance**: G# aims to achieve parity with raw CUDA C++ performance through direct syntax mapping and leveraging the highly optimized PTX compilation pipeline at build time.

### 1.1.2 Context Terminology (Host C# vs G# Kernel)

Because G# uses C# syntax to write GPU kernels, this specification uses the following distinct terms to clarify whether a concept applies to the CPU host or the GPU device:

- **Host C# (CPU Context)**: Standard C# code running on the .NET Core CLR. This code orchestrates GPU execution, allocates memory, and dispatches kernels.
- **G# Kernel (GPU Context)**: A strict, limited subset of C# syntax used inside methods decorated with `[GpuKernel]` or `[GpuDeviceFunction]`. This code is **not** executed by the .NET runtime; it is transpiled into CUDA C++ during the AOT phase and executed directly on the GPU.

## 1.2 Architecture: Pure AOT Static Execution Model

The G# framework strictly operates on a **Pure AOT (Ahead-Of-Time) Static Execution Model**. All C# kernel logic is completely resolved and compiled during the .NET build phase.

- The **AOT Generator (`Kassyi.Generators.GSharp`)** translates C# syntax into CUDA C++ and orchestrates the native compilation into machine code (`.fatbin`) during the standard MSBuild process.
- The **Runtime Loader (`Kassyi.GSharp.Runtime`)** serves as an ultra-lightweight loader that validates binary integrity (via [trailing hash verification](../02_architecture/02_runtime_loader.md#22-aot-module-loading-and-validation)) and loads the precompiled AOT binaries directly into the NVIDIA driver, instantly dispatching the kernel with zero compilation overhead.

## 1.3 Transpiler Dialect Architecture (Two-Tier Design)

A defining strength of G# is its **Two-Tier Transpiler Architecture**. Rather than hardcoding C# directly to NVIDIA CUDA C++, G# separates the Abstract Syntax Tree (AST) analysis engine from the backend syntax generation via the `ITranspilerDialect` interface.

- **Primary Dialect**: The default `CudaCppDialect` acts as a language overlay, mapping C# constructs to highly optimized NVIDIA CUDA C++ and PTX.
- **Backend Agnosticism**: By simply swapping the active Dialect, the identical Roslyn AST engine can target entirely different GPU backends (e.g., OpenCL C, Apple Metal Shading Language, or Vulkan GLSL) without rewriting the core compilation logic.

> For deep internal architecture details of the transpiler and runtime execution, refer to **Part II: Implementation Overview** (`../02_architecture/01_aot_transpiler.md` and `../02_architecture/02_runtime_loader.md`).
