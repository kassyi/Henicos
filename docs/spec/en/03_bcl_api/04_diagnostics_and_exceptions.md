---
title: "7. Diagnostics and Exceptions"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "The G# compilation and execution pipelines impose strict boundaries between host CPU code and device GPU kernels. This document details how excepti..."
---

# 7. Diagnostics and Exceptions

The G# compilation and execution pipelines impose strict boundaries between host CPU code and device GPU kernels. 

> [!NOTE]
> **For detailed architectural explanations:**
> To understand why standard exceptions (`throw new Exception()`) are prohibited on the GPU, how `GpuOnlyException` works, and how the Roslyn Analyzer (`RS0030`) enforces these constraints, please refer to the **[Exception Handling and Diagnostics Guide](../04_guides/03_exception_handling_guide.md)**.

---

## 7.1 Transpiler Diagnostic Codes (`GSHxxxx`)

During the MSBuild AOT code generation phase, the `Kassyi.Generators.GSharp` Roslyn Source Generator analyzes the AST of your kernels. If it encounters syntax or types that cannot be transpiled to CUDA, it emits custom `GSH` diagnostic errors.

> [!TIP]
> For detailed Before/After code examples and remediation guidelines for each diagnostic, explore the [Diagnostics Reference Catalog](diagnostics/00_index.md).
> For language syntax constraints, refer to [1.5 Limitations](../01_language/05_limitations.md).

| ID                                    | Severity | Description & Resolution                                                                                                                                                                          |
| :------------------------------------ | :------- | :------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| [**GSH0001**](diagnostics/GSH0001.md) | Error    | **Object creation not supported.** You attempted to use `new Class()`. Remove the allocation or use a `struct`.                                                                                   |
| [**GSH0002**](diagnostics/GSH0002.md) | Error    | **String usage not supported.** You attempted to use the `string` type.                                                                                                                           |
| [**GSH0003**](diagnostics/GSH0003.md) | Error    | **Unsupported syntax.** Triggered when unknown/unsupported C# syntax nodes are encountered.                                                                                                       |
| [**GSH0004**](diagnostics/GSH0004.md) | Error    | **Unsupported Return Type.** A `[GpuKernel]` method must return `void` or a supported value type.                                                                                                 |
| [**GSH0005**](diagnostics/GSH0005.md) | Error    | **GpuBigIntExpr Direct Assignment.** Triggered when a `GpuBigIntExpr` is assigned to a local variable without being resolved into `.Value`. Prevents silent "computation lost" bugs.              |
| [**GSH0006**](diagnostics/GSH0006.md) | Error    | **InlinePtx non-constant argument.** Triggered when `Gpu.InlinePtx` receives a dynamic string instead of a compile-time string literal.                                                           |
| [**GSH0007**](diagnostics/GSH0007.md) | Error    | **foreach on unsupported collection.** Triggered when iterating over types requiring a heap enumerator (e.g. `IEnumerable<T>`). Arrays, `Span<T>`, and `[InlineArray]` are fully supported.       |
| [**GSH0008**](diagnostics/GSH0008.md) | Error    | **LINQ syntax not supported.** LINQ relies on delegates and dynamic allocations. Use imperative loops instead.                                                                                    |
| [**GSH0009**](diagnostics/GSH0009.md) | Error    | **Implicit var requires explicit literal suffix.** Triggered when `var` is initialized with an untyped bare literal (e.g., `var x = 0;`), which can cause C++ `auto` type inference bugs.         |
| [**GSH0010**](diagnostics/GSH0010.md) | Error    | **GSharp Prelude missing.** Triggered when the AOT transpiler fails to locate the necessary C++ runtime prelude headers.                                                                          |
| [**GSH0011**](diagnostics/GSH0011.md) | Error    | **Macro lambda requires a parameter.** Triggered when a `[GpuOperatorMacro]` lambda mapping does not declare expected parameters.                                                                 |
| [**GSH0012**](diagnostics/GSH0012.md) | Error    | **Macro arguments must be compile-time constants.** Triggered when a macro argument cannot be folded into a constant value during transpilation.                                                  |
| [**GSH0013**](diagnostics/GSH0013.md) | Warning  | **Excessive static shared memory.** A `SharedMemory<T>` allocation exceeds 32KB. This can severely reduce SM warp occupancy.                                                                      |
| [**GSH0014**](diagnostics/GSH0014.md) | Warning  | **Large struct passed by value.** A struct parameter exceeds 32 bytes. Pass it via `ref` or as an array to save limited constant memory space.                                                    |
| [**GSH0015**](diagnostics/GSH0015.md) | Warning  | **Auto-allocate return in loop.** You called a kernel that returns an array from inside a `for` loop on the host. This allocates a new unmanaged buffer every iteration.                          |
| [**GSH0016**](diagnostics/GSH0016.md) | Error    | **Unroll factor must be a non-negative compile-time constant.** The argument `factor` of `Gpu.Hint.Unroll` must be a compile-time non-negative integer.                                           |
| [**GSH0017**](diagnostics/GSH0017.md) | Error    | **Unroll hint must precede a loop.** `Gpu.Hint.Unroll` must immediately precede a `for`, `while`, or `do-while` loop.                                                                             |
| [**GSH0018**](diagnostics/GSH0018.md) | Error    | **GPU intrinsic called from host.** Triggered when a GPU-specific intrinsic is invoked from a CPU host method.                                                                                    |
| [**GSH0019**](diagnostics/GSH0019.md) | Error    | **Manual GetEnumerator calls are not supported.** Explicit calls to `GetEnumerator()` are prohibited to ensure zero-allocation. Use a `foreach` loop over supported types like `Span<T>` instead. |
| [**GSH0020**](diagnostics/GSH0020.md) | Error    | **Missing IDisposable implementation.** A class holds a disposable GPU resource (e.g., `GpuContext`) but does not implement `IDisposable`.                                                        |
| [**GSH0021**](diagnostics/GSH0021.md) | Error    | **Undisposed GPU Resource.** The `Dispose()` method does not explicitly invoke `.Dispose()` on all owned GPU resources.                                                                           |
| [**GSH0022**](diagnostics/GSH0022.md) | Error    | **Unsafe field overwrite.** A disposable field is overwritten without prior disposal.                                                                                                             |
| [**GSH0023**](diagnostics/GSH0023.md) | Error    | **Property getter side-effect.** Property getters must not allocate disposable resources.                                                                                                         |
| [**GSH0024**](diagnostics/GSH0024.md) | Error    | **Cast operator side-effect.** Implicit or explicit cast operators must not allocate disposable resources.                                                                                        |
| [**GSH0025**](diagnostics/GSH0025.md) | Error    | **Unguarded multi-allocation.** Multiple allocations in a constructor must be guarded with try-catch to avoid partial leaks.                                                                      |
| [**GSH0026**](diagnostics/GSH0026.md) | Error    | **Static disposable allocation.** Static fields must not allocate disposable resources.                                                                                                           |
| [**GSH0027**](diagnostics/GSH0027.md) | Error    | **Disposing borrowed resource.** Disposing a borrowed resource creates a double-dispose hazard. If taking ownership, use `[TakesOwnership]`.                                                      |
| [**GSH0028**](diagnostics/GSH0028.md) | Error    | **Disposable resource in Default Interface Method.** Default Interface Methods (DIM) cannot take ownership or dispose of resources.                                                               |
| [**GSH0029**](diagnostics/GSH0029.md) | Warning  | **Manual unmanaged allocation detected.** You invoked manual unmanaged allocation (e.g., `Gpu.Unsafe.Malloc()`). Ensure corresponding `Free()` is called to prevent VRAM leaks.                   |
| [**RS0030**](diagnostics/RS0030.md)   | Error    | **Banned API in GPU kernel.** Triggered when a banned .NET API is used in GPU kernels or GPU intrinsics are invoked from host CPU code.                                                           |
