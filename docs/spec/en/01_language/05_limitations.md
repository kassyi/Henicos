# 5. Limitations and Diagnostics

This document outlines the strict limitations imposed on C# code executed within the G# framework and details the diagnostic strategies employed to enforce these constraints.

## 5.1 Prohibited C# Features

The CUDA execution environment lacks a garbage collector, a robust runtime type information (RTTI) system, and standard .NET libraries. Consequently, the following C# features are explicitly prohibited inside `[GpuKernel]` methods:

| Feature | Reason for Prohibition / Status |
| :--- | :--- |
| **Object Instantiation (`new`)** | Lack of a managed heap and garbage collector on the GPU. (Note: Stack-allocated `struct` instantiation, `SharedMemory<T>`, `stackalloc` implicit arrays, and C# 12 Collection Expressions `[]` are exceptions and fully supported). |
| **Strings (`string`)** | Strings are managed reference types. Fixed-size character arrays must be used instead, though not natively supported yet. |
| **LINQ (`System.Linq`)** | Relies heavily on delegates, iterators, and dynamic allocations. (Enforced by **GSH0008**) |
| **`foreach` Loops** | Implicitly allocates `IEnumerator` instances on the managed heap, which is generally incompatible with GPU memory execution. However, iterating over `Span<T>`, `ReadOnlySpan<T>`, `InlineArray`, or fixed arrays does NOT allocate and is explicitly supported. (Enforced by **GSH0007**) |
| **Virtual Methods / Interfaces** | Dynamic dispatch (v-tables) is unsupported in the current AOT transpiler. |
| **Exceptions (`throw`, `try-catch`)** | Exception unwinding is not supported in CUDA device code. |
| **Recursion** | Heavily restricted due to the highly limited thread stack size (typically a few kilobytes per thread) on GPUs. |
| **Reflection** | The .NET reflection metadata is entirely absent on the GPU. |
| **Generics** | True runtime generics are unsupported. However, **C++ Template generation is fully supported** for C# generic structs and methods, allowing the AOT transpiler to map C# generics directly to highly optimized C++ templates (`template <typename T>`). |
| **Expression-Bodied Members (`=>`)** | **Supported.** Transpiler automatically handles `void` and value-returning methods (injects `return` keyword). |
| **Ternary Operator (`? :`)** | Standard conditional ternary operators are supported. |
| **Braceless `if` Statements** | **Supported.** Transpiler automatically injects `{ }` block scoping into the C++ source for single-statement control flow branches. |
| **Delegates & Lambda Expressions** | **Partially Supported.** Non-capturing lambdas and local functions, or those capturing only value types, are extracted and expanded as C++ `__device__` functions (or lambdas). Complex closures requiring managed heap allocations (e.g., capturing reference types) are not supported. |
| **`switch` Expressions** | **Partially Supported.** Only `ConstantPattern` (literals) and `DiscardPattern` (`_`) are supported. Using `when` clauses or complex pattern matching (like property patterns) will result in a compiler error (`UnsupportedSyntax`). |
| **`ref switch` Expressions** | **Unsupported by C#.** C# does not currently support `ref switch` expressions (`ref var r = x switch { ... }`). Use `if/else` or `ref ? :` for conditional `ref` assignment. |

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

The `Kassyi.Generators.GSharp` AOT pipeline enforces constraints natively during compilation by traversing the C# AST and reporting specific MSBuild diagnostics:

| ID | Severity | Title | Condition |
| :--- | :--- | :--- | :--- |
| **GSH0001** | Error | Object creation not supported | Triggered when `new` or `new()` is used on anything other than `SharedMemory<T>` or unmanaged `struct` types. |
| **GSH0002** | Error | String usage not supported | Triggered when the `string` keyword is used. |
| **GSH0003** | Error | Unsupported syntax | Triggered when unknown/unsupported C# syntax nodes are encountered. |
| **GSH0004** | Error | GpuKernel unsupported return type | Triggered when a `[GpuKernel]` method declares an unsupported return type (like `string` or arrays). Only `void` and unmanaged value types are supported. |
| **GSH0005** | Error | GpuBigIntExpr Direct Assignment | Triggered when a `GpuBigIntExpr` (from lazy evaluation operators) is assigned to a local variable (`var x = a + b;`) without being resolved into `.Value`. Prevents silent "computation lost" bugs. |
| **GSH0006** | Error | InlinePtx non-constant argument | Triggered when `Gpu.InlinePtx` receives a dynamic string instead of a compile-time string literal. |
| **GSH0007** | Error | `foreach` loop not supported | Triggered when a `foreach` loop is used on a type that allocates an `IEnumerator`. Spans and Arrays are safely excluded. |
| **GSH0008** | Error | LINQ syntax not supported | Triggered when LINQ query syntax or methods are used. |
| **GSH0009** | Error | Implicit var requires explicit literal suffix | Triggered when `var` is initialized with an untyped bare literal (e.g., `var x = 0;`), which can cause C++ `auto` type inference bugs. |
| **GSH0010** | Error | GSharp Prelude missing | Triggered when the AOT transpiler fails to locate the necessary C++ runtime prelude headers. |
| **GSH0011** | Error | Macro lambda requires a parameter | Triggered when a `[GpuOperatorMacro]` lambda mapping does not declare expected parameters. |
| **GSH0012** | Error | Macro arguments must be compile-time constants | Triggered when a macro argument cannot be folded into a constant value during transpilation. |
| **GSH0013** | Warning | Excessive static shared memory | Triggered when a static `SharedMemory<T>` allocation exceeds 32,768 bytes, which can reduce warp occupancy. |
| **GSH0014** | Warning | Large struct passed by value | Triggered when a custom struct $\ge$ 32 bytes is passed by value. Advises passing by array pointer or `ref` to conserve constant memory. |
| **GSH0015** | Warning | Auto-allocate return in loop | Triggered when a kernel launch method that triggers an **auto-allocated array return** is invoked from within a host CPU loop (`for`, `while`, `foreach`). This prevents unbounded implicit device buffer allocations per iteration. |
| **GSH0016** | Error | CudaCpp Dialect Not Active | Triggered when a CUDA-specific hardware intrinsic is invoked while the `CpuFallback` dialect is active. |
| **RS0030** | Error | GPU intrinsic invoked from host code | Triggered via `BannedApiAnalyzers` when a GPU-specific intrinsic (e.g., `ThreadIndex.X`, `Gpu.SyncThreads()`) is illegally invoked from standard CPU host code. |

