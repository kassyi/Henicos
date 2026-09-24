---
title: "3. Exception Handling and Diagnostics Guide"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Detailed architectural explanation of exception handling boundaries between host CPU code and device GPU kernels, and how the analyzer enforces them."
---

# 3. Exception Handling and Diagnostics Guide

The G# compilation and execution pipelines impose strict boundaries between host CPU code and device GPU kernels. Because of the fundamental architectural differences between CPUs and NVIDIA GPUs, the standard C# exception model cannot be transpiled directly.

This guide details why exceptions are prohibited on the GPU, how the `GpuOnlyException` pattern works, and how the Roslyn Analyzer (`RS0030`) enforces these constraints.

---

## 3.1 The "No Exceptions" Rule

NVIDIA GPUs and the CUDA execution model do not support standard C++ or C# exception unwinding (e.g., `try`, `catch`, `finally`, `throw`).

If a hardware fault occurs (such as an illegal memory access or an out-of-bounds array read), the GPU hardware generates a trap, which halts the kernel and returns a failure code (`CUDA_ERROR_ILLEGAL_ADDRESS`) to the host. The host `.NET` runtime will then throw a standard `CudaException`.

> [!IMPORTANT]
> **You cannot write `throw new Exception()` inside a G# kernel.**
> Doing so requires managed heap allocation (for the Exception object itself) and relies on runtime type information (RTTI) and stack unwinding, none of which exist on the GPU device.

---

## 3.2 `GpuOnlyException`

You will notice that all intrinsic methods in `Kassyi.GSharp.Core` (e.g., `Gpu.SyncThreads()`, `ThreadIndex.X`) throw a `GpuOnlyException`.

```csharp
// Execution Context: Host (CPU)
public static int X => GpuOnlyException.Throw<int>();
```

**Why does this exist if exceptions are prohibited?**
These methods are designed to be intercepted and replaced by the G# AOT Transpiler _at compile-time_.
If you accidentally call `ThreadIndex.X` from standard CPU code (e.g., inside `Main()`), the transpiler never intercepts it. Thus, the real C# method executes, immediately throwing the `GpuOnlyException` to prevent undefined CPU behavior.

---

## 3.3 Analyzer Enforcement (`RS0030`)

To prevent the usage of inherently incompatible .NET libraries inside GPU kernels, G# utilizes the `Microsoft.CodeAnalysis.BannedApiAnalyzers` package.

For example, attempting to use standard LINQ extension methods (which heavily rely on dynamic memory allocation and delegates) inside a `[GpuKernel]` will trigger an immediate compiler error:

**Error RS0030: The API 'System.Linq.Enumerable' is banned in this project.**

> [!TIP]
> For the comprehensive reference table of Transpiler Diagnostic Codes (GSHxxxx), see the [Diagnostics Reference Catalog](../03_bcl_api/01_diagnostics_and_exceptions.md).

## See also




### Language Specification

- [1. Language Specification](../01_language/00_index.md)
- [5. Limitations and Diagnostics](../01_language/05_limitations.md)

### BCL API

- [BCL API and Hardware Intrinsics Reference](../03_bcl_api/00_index.md)
- [7. Diagnostics and Exceptions](../03_bcl_api/01_diagnostics_and_exceptions.md)

### BCL API > Diagnostics

- [Transpiler and Analyzer Diagnostics (GSHxxxx)](../03_bcl_api/diagnostics/00_index.md)

