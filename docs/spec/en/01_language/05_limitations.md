---
title: "5. Limitations and Diagnostics"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "This document outlines the strict limitations imposed on C# code executed within the G# framework and details the diagnostic strategies employed to..."
---

# 5. Limitations and Diagnostics

This document outlines the strict limitations imposed on C# code executed within the G# framework and details the diagnostic strategies employed to enforce these constraints.

> [!IMPORTANT]
> **Host vs. Device Boundaries**
> G# differentiates between standard C# running on the **Host (CPU)** and C# translated into a **G# Kernel (GPU)**. Most of the limitations listed below apply *only* to code written inside a `[GpuKernel]` method. You are free to use standard C# features (like Strings and LINQ) in your regular CPU code.

## 5.1 Prohibited C# Features inside [GpuKernel]

The CUDA execution environment lacks a garbage collector, a robust runtime type information (RTTI) system, and standard .NET libraries. Consequently, the following C# features are explicitly prohibited **inside `[GpuKernel]` methods** (Device execution):

| Feature | Limitation Context | Reason for Prohibition / Status |
| :--- | :--- | :--- |
| **Object Instantiation (`new`)** | **Device Only** | Lack of a managed heap and garbage collector on the GPU. (Note: Stack-allocated `struct` instantiation, `SharedMemory<T>`, `stackalloc` implicit arrays, and C# 12 Collection Expressions `[]` are exceptions and fully supported). |
| **Strings (`string`)** | **Device Only** | Strings are managed reference types. Fixed-size character arrays must be used instead, though not natively supported yet. (Fine to use on Host). |
| **Console I/O (`Console.WriteLine`)** | **Device Only** | `Console.WriteLine` and standard I/O streams are completely unavailable on the GPU. To inspect variable state during debugging, use a pre-allocated telemetry array (see [Guide: Print Debugging](../04_guides/06_antipatterns_and_best_practices.md#6-print-debugging-the-consolewriteline-trap)). |
| **LINQ (`System.Linq`)** | **Device Only** | Relies heavily on delegates, iterators, and dynamic allocations. (Enforced by **GSH0008**) |
| **`foreach` Loops** | **Device Only** | Implicitly allocates `IEnumerator` instances on the managed heap, which is generally incompatible with GPU memory execution. However, iterating over `Span<T>`, `ReadOnlySpan<T>`, `InlineArray`, or fixed arrays does NOT allocate and is explicitly supported. (Enforced by **GSH0007**) |
| **Virtual Methods / Interfaces** | **Device Only** | Dynamic dispatch (v-tables) is unsupported in the current AOT transpiler. |
| **Exceptions (`throw`, `try-catch`)** | **Device Only** | Exception unwinding is not supported in CUDA device code. |
| **Recursion** | **Device Only** | Heavily restricted due to the highly limited thread stack size (typically a few kilobytes per thread) on GPUs. |
| **Reflection** | **Device Only** | The .NET reflection metadata is entirely absent on the GPU. |
| **Generics** | **Device Only (Partial)** | True runtime generics are unsupported. However, **C++ Template generation is fully supported** for C# generic structs and methods, allowing the AOT transpiler to map C# generics directly to highly optimized C++ templates (`template <typename T>`). |
| **Expression-Bodied Members (`=>`)** | **Supported** | Transpiler automatically handles `void` and value-returning methods (injects `return` keyword). |
| **Ternary Operator (`? :`)** | **Supported** | Standard conditional ternary operators are supported. |
| **Braceless `if` Statements** | **Supported** | Transpiler automatically injects `{ }` block scoping into the C++ source for single-statement control flow branches. |
| **Delegates & Lambda Expressions** | **Device Only (Partial)** | Non-capturing lambdas and local functions, or those capturing only value types, are extracted and expanded as C++ `__device__` functions (or lambdas). Complex closures requiring managed heap allocations (e.g., capturing reference types) are not supported. |
| **`switch` Expressions** | **Device Only (Partial)** | Only `ConstantPattern` (literals) and `DiscardPattern` (`_`) are supported. Using `when` clauses or complex pattern matching (like property patterns) will result in a compiler error (`UnsupportedSyntax`). |
| **`ref switch` Expressions** | **Unsupported by C#** | C# does not currently support `ref switch` expressions (`ref var r = x switch { ... }`). Use `if/else` or `ref ? :` for conditional `ref` assignment. |

## 5.2 Dialect Syntax Support

While `CudaCppDialect` fully supports all mapped GPU hardware intrinsics, the default `CpuDialect` (which provides native CPU-fallback for tests) has structural limitations due to its OpenMP and `std::thread` foundation:

* **Device Synchronization (`Gpu.SyncThreads()`, `__syncthreads()`)**: CPU threads cannot perform hardware warp-level barriers. While they may compile, using these barriers can cause severe race conditions or deadlocks during CPU fallback execution.
* **Warp Shuffles (`__shfl_sync()`, etc.)**: Hardware warp intrinsics are completely unsupported by `CpuDialect` and will cause C++ compilation failures during the CPU native library build phase.

If a kernel relies on these features, developers must restrict it to hardware execution or provide alternative atomic algorithms when testing on the CPU fallback pipeline.

## 5.3 Standard Diagnostics and Warnings

Because G# utilizes the standard Roslyn C# compiler to validate syntax before transpilation, certain standard C# rules must be carefully managed.

### 5.3.1 Suppressing Pointer Warnings
To manipulate raw memory arrays (e.g., `int*`), G# kernels must be marked with the `unsafe` keyword. Developers should configure their `.csproj` to allow unsafe blocks (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`).

### 5.3.2 Dereference Warnings (`CS8602`)
When utilizing C# 8.0+ Nullable Reference Types, passing raw pointers might trigger `CS8602` (Dereference of a possibly null reference).
Since all pointer validity checks are pushed to the AOT/Runtime phase, developers may need to suppress these warnings locally via `#pragma warning disable CS8602` or configure analyzer rules appropriately.

## 5.4 Custom G# Diagnostics

The `Kassyi.Generators.GSharp` AOT pipeline enforces constraints natively during compilation by traversing the C# AST and reporting specific MSBuild diagnostics. Click any Diagnostic ID for detailed causes, solutions, and Before/After code examples:

| ID | Severity | Title | Condition |
| :--- | :--- | :--- | :--- |
| [**GSH0001**](../03_bcl_api/diagnostics/GSH0001.md) | Error | Object creation not supported | Triggered when `new` or `new()` is used on anything other than `SharedMemory<T>` or unmanaged `struct` types. |
| [**GSH0002**](../03_bcl_api/diagnostics/GSH0002.md) | Error | String usage not supported | Triggered when the `string` keyword is used inside a kernel. |
| [**GSH0003**](../03_bcl_api/diagnostics/GSH0003.md) | Error | Unsupported syntax | Triggered when unknown/unsupported C# syntax nodes are encountered in device code. |
| [**GSH0004**](../03_bcl_api/diagnostics/GSH0004.md) | Error | GpuKernel unsupported return type | Triggered when a `[GpuKernel]` method declares an unsupported return type. Only `void` and unmanaged value types are supported. |
| [**GSH0005**](../03_bcl_api/diagnostics/GSH0005.md) | Error | GpuBigIntExpr Direct Assignment | Triggered when a `GpuBigIntExpr` is assigned to a local variable (`var x = a + b;`) without being resolved into `.Value`. |
| [**GSH0006**](../03_bcl_api/diagnostics/GSH0006.md) | Error | InlinePtx non-constant argument | Triggered when `Gpu.InlinePtx` receives a dynamic string instead of a compile-time string literal. |
| [**GSH0007**](../03_bcl_api/diagnostics/GSH0007.md) | Error | `foreach` on unsupported collection | Triggered when `foreach` is used on types allocating an `IEnumerator` (e.g. `IEnumerable<T>`). Arrays, `Span<T>`, and `[InlineArray]` are fully supported. |
| [**GSH0008**](../03_bcl_api/diagnostics/GSH0008.md) | Error | LINQ syntax not supported | Triggered when LINQ query syntax or methods are used in device code. |
| [**GSH0009**](../03_bcl_api/diagnostics/GSH0009.md) | Error | Implicit var requires explicit literal suffix | Triggered when `var` is initialized with an untyped bare literal (e.g., `var x = 0;`). |
| [**GSH0010**](../03_bcl_api/diagnostics/GSH0010.md) | Error | GSharp Prelude missing | Triggered when the AOT transpiler fails to locate the necessary C++ runtime prelude headers. |
| [**GSH0011**](../03_bcl_api/diagnostics/GSH0011.md) | Error | Macro lambda requires a parameter | Triggered when a macro lambda mapping does not declare expected parameters. |
| [**GSH0012**](../03_bcl_api/diagnostics/GSH0012.md) | Error | Macro arguments must be compile-time constants | Triggered when a macro argument cannot be folded into a constant value during transpilation. |
| [**GSH0013**](../03_bcl_api/diagnostics/GSH0013.md) | Warning | Excessive static shared memory | Triggered when a static `SharedMemory<T>` allocation exceeds 32,768 bytes, which can reduce warp occupancy. |
| [**GSH0014**](../03_bcl_api/diagnostics/GSH0014.md) | Warning | Large struct passed by value | Triggered when a custom struct $\ge$ 32 bytes is passed by value. Advises passing by reference (`ref`) or pointer. |
| [**GSH0015**](../03_bcl_api/diagnostics/GSH0015.md) | Warning | Auto-allocate return in loop | Triggered when an auto-allocated array return kernel is invoked from within a host CPU loop. |
| [**GSH0016**](../03_bcl_api/diagnostics/GSH0016.md) | Error | Unroll factor must be non-negative constant | Triggered when `factor` in `Gpu.Hint.Unroll` is negative or non-constant. |
| [**GSH0017**](../03_bcl_api/diagnostics/GSH0017.md) | Error | Unroll hint must precede a loop | Triggered when `Gpu.Hint.Unroll` does not immediately precede a `for`, `while`, or `do-while` loop. |
| [**GSH0018**](../03_bcl_api/diagnostics/GSH0018.md) | Error | GPU intrinsic called from host | Triggered when a GPU-specific intrinsic is invoked from standard CPU host code. |
| [**GSH0019**](../03_bcl_api/diagnostics/GSH0019.md) | Error | Manual GetEnumerator not supported | Triggered when explicit calls to `GetEnumerator()` are made in device code. |
| [**GSH0020**](../03_bcl_api/diagnostics/GSH0020.md) | Error | Missing IDisposable implementation | Triggered when a class holds an owned disposable GPU resource but does not implement `IDisposable`. |
| [**GSH0021**](../03_bcl_api/diagnostics/GSH0021.md) | Error | Undisposed GPU resource | Triggered when `Dispose()` fails to dispose all owned GPU resources. |
| [**GSH0022**](../03_bcl_api/diagnostics/GSH0022.md) | Error | Unsafe field overwrite | Triggered when a disposable GPU resource field is overwritten without prior disposal. |
| [**GSH0023**](../03_bcl_api/diagnostics/GSH0023.md) | Error | Property getter side-effect | Triggered when a property getter allocates a disposable GPU resource. |
| [**GSH0024**](../03_bcl_api/diagnostics/GSH0024.md) | Error | Cast operator side-effect | Triggered when a cast operator allocates a disposable GPU resource. |
| [**GSH0025**](../03_bcl_api/diagnostics/GSH0025.md) | Error | Unguarded multi-allocation | Triggered when multiple allocations in a constructor are not guarded with try-catch. |
| [**GSH0026**](../03_bcl_api/diagnostics/GSH0026.md) | Error | Static disposable allocation | Triggered when a static field allocates a disposable GPU resource. |
| [**GSH0027**](../03_bcl_api/diagnostics/GSH0027.md) | Error | Disposing borrowed resource | Triggered when disposing a borrowed resource parameter. |
| [**GSH0028**](../03_bcl_api/diagnostics/GSH0028.md) | Error | Disposable resource in DIM | Triggered when Default Interface Methods attempt to manage disposable resources. |
| [**GSH0029**](../03_bcl_api/diagnostics/GSH0029.md) | Warning | Manual unmanaged allocation | Triggered when low-level manual unmanaged allocation (`Gpu.Unsafe.Malloc`) is detected. |
| [**RS0030**](../03_bcl_api/diagnostics/RS0030.md) | Error | Banned API in GPU kernel | Triggered via `BannedApiAnalyzers` when a banned API is used in GPU kernels or GPU intrinsics in host code. |

For individual guides, see the [Diagnostics Reference Catalog](../03_bcl_api/diagnostics/00_index.md).

## See also

- [1. Language Specification Overview](00_index.md)
- [1. Language Introduction](01_introduction.md)
- [4. Execution and Concurrency Model](04_execution_model.md)
- [Gpu BCL API Overview](../03_bcl_api/00_index.md)
- [Diagnostics and Exceptions Reference](../03_bcl_api/01_diagnostics_and_exceptions.md)
- [Diagnostics Reference Index](../03_bcl_api/diagnostics/00_index.md)
- [Antipatterns and Best Practices Guide](../04_guides/06_antipatterns_and_best_practices.md)
