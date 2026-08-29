# 1. Foundation and Domain

---

## G# Core Design Philosophy & Manifest

> **"Bringing C#'s existing mental model directly into the GPU realm delivers the ultimate developer experience (DX)."**

G# (Henicos) strictly adheres to three uncompromising technical tenets:

1. **YES AST (Roslyn Syntax Nodes & Semantic Models)**: All code analysis, transpilation, and safety enforcement are performed strictly via Roslyn Abstract Syntax Trees and Semantic Models at compile-time.
2. **YES Zero-Allocation**: GPU device-side execution fundamentally enforces a zero-allocation guarantee—prohibiting dynamic reference-type heap allocations while fully leveraging value types (`struct`) and stack/register memory.
3. **YES Static Determinism**: All kernel logic and types must be fully resolved at compile-time with zero runtime overhead—**NO Reflection, NO LINQ, and NO Dynamic Dispatch**.

---

## 1.1 G# Definition and Scope

This document defines the foundational concepts, scope, and hybrid compilation architecture of the G# (GSharp) transpiler and runtime framework.

G# is a domain-specific C#-to-CUDA transpilation engine and runtime environment. It enables developers to write GPU compute kernels using standard C# syntax, which are then intercepted, translated into CUDA C++ / PTX, and executed natively on NVIDIA GPUs.

G# is **not** a general-purpose programming language. It is a highly restricted subset of C# designed strictly for Massively Parallel Processor (MPP) architectures.

### 1.1.1 Core Principles

- **Zero-Allocation**: G# execution occurs entirely on the GPU device. Heap allocations (`new Class()`, `string`) are strictly prohibited within the kernel context.
- **Deterministic Execution**: Kernel logic must be statically analyzable at compile time. Dynamic dispatch, reflection, and runtime type inference are unsupported.
- **Native Performance**: G# aims to achieve parity with raw CUDA C++ performance through direct syntax mapping and aggressive Just-In-Time (JIT) optimization at the NVIDIA driver level.

### 1.1.2 Context Terminology (Host C# vs G# Kernel)

Because G# uses C# syntax to write GPU kernels, this specification uses the following distinct terms to clarify whether a concept applies to the CPU host or the GPU device:

- **Host C# (CPU Context)**: Standard C# code running on the .NET Core CLR. This code orchestrates GPU execution, allocates memory, and dispatches kernels.
- **G# Kernel (GPU Context)**: A strict, limited subset of C# syntax used inside methods decorated with `[GpuKernel]` or `[GpuDeviceFunction]`. This code is **not** executed by the .NET runtime; it is transpiled into CUDA C++ during the AOT phase and executed directly on the GPU.

## 1.2 Architecture: AOT + JIT Hybrid Model

The G# framework operates on a hybrid pipeline combining Ahead-Of-Time (AOT) static generation and Just-In-Time (JIT) compilation.

- The **AOT Generator (`Kassyi.Generators.GSharp`)** translates C# syntax into CUDA C++ during the standard .NET build process and generates the host-side C# launcher.
- The **JIT Runtime (`Kassyi.GSharp.Runtime`)** compiles the C++ source dynamically using the NVIDIA CUDA Compiler (`nvcc`) at runtime and dispatches the kernel.

> For deep internal architecture details of the transpiler and runtime execution, refer to **Part II: Implementation Overview** (`07_aot_generator_architecture.md` and `08_jit_runtime_pipeline.md`).
