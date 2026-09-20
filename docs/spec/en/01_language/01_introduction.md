# 1. Foundation and Domain

---

## G# Core Design Philosophy & Manifest

> **"Bringing C#'s existing mental model directly into the GPU realm delivers the ultimate developer experience (DX)."**

G# strictly adheres to three uncompromising technical tenets:

1. **YES AST (Roslyn Syntax Nodes & Semantic Models)**: All code analysis, transpilation, and safety enforcement are performed strictly via Roslyn Abstract Syntax Trees and Semantic Models at compile-time.
2. **YES Zero-Allocation**: GPU device-side execution fundamentally enforces a zero-allocation guarantee—prohibiting dynamic reference-type heap allocations while fully leveraging value types (`struct`) and stack/register memory.
3. **YES Static Determinism**: All kernel logic and types must be fully resolved at compile-time with zero runtime overhead—**NO Reflection, NO LINQ, and NO Dynamic Dispatch**.

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
- The **Runtime Loader (`Kassyi.GSharp.Runtime`)** serves as an ultra-lightweight loader that validates, decrypts, and loads the precompiled AOT binaries directly into the NVIDIA driver, instantly dispatching the kernel with zero compilation overhead.

## 1.3 Transpiler Dialect Architecture (Two-Tier Design)

A defining strength of G# is its **Two-Tier Transpiler Architecture**. Rather than hardcoding C# directly to NVIDIA CUDA C++, G# separates the Abstract Syntax Tree (AST) analysis engine from the backend syntax generation via the `ITranspilerDialect` interface.

- **Primary Dialect**: The default `CudaCppDialect` acts as a language overlay, mapping C# constructs to highly optimized NVIDIA CUDA C++ and PTX.
- **Backend Agnosticism**: By simply swapping the active Dialect, the identical Roslyn AST engine can target entirely different GPU backends (e.g., OpenCL C, Apple Metal Shading Language, or Vulkan GLSL) without rewriting the core compilation logic.

> For deep internal architecture details of the transpiler and runtime execution, refer to **Part II: Implementation Overview** (`../02_architecture/01_aot_transpiler.md` and `../02_architecture/02_runtime_loader.md`).
