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
| **`decimal`** | **Device Only** | `decimal` arithmetic is software in the .NET runtime, with no C++ or CUDA type or literal. Each use in device code (the keyword, a `1.5m` literal, a reference to a `decimal` field, local, parameter or property, a call that returns `decimal`) is reported. Convert to `double` on the host. (Enforced by **GSH0003**) |
| **Console I/O (`Console.WriteLine`)** | **Device Only** | `Console.WriteLine` and standard I/O streams are completely unavailable on the GPU. To inspect variable state during debugging, use a pre-allocated telemetry array (see [Guide: Print Debugging](../04_guides/06_antipatterns_and_best_practices.md#6-print-debugging-the-consolewriteline-trap)). |
| **LINQ (`System.Linq`)** | **Device Only** | Relies heavily on delegates, iterators, and dynamic allocations. (Enforced by **GSH0008**) |
| **`foreach` Loops** | **Device Only** | Implicitly allocates `IEnumerator` instances on the managed heap, which is generally incompatible with GPU memory execution. However, iterating over `Span<T>`, `ReadOnlySpan<T>`, `InlineArray`, fixed arrays, or a `[GpuDeviceFunction]` iterator (`IEnumerable<T>` with `yield`, lowered by inlining the iterator body at each `foreach`) does NOT allocate and is explicitly supported. (Enforced by **GSH0007**, **GSH0070**–**GSH0073**) |
| **Virtual Methods / Interfaces** | **Device Only** | Dynamic dispatch (v-tables) is unsupported in the current AOT transpiler. |
| **Exceptions (`throw`, `try-catch`)** | **Device Only** | Exception unwinding is not supported in CUDA device code. |
| **Recursion** | **Device Only** | Heavily restricted due to the highly limited thread stack size (typically a few kilobytes per thread) on GPUs. |
| **Reflection** | **Device Only** | The .NET reflection metadata is entirely absent on the GPU. |
| **Generics** | **Device Only (Partial)** | True runtime generics are unsupported. However, **C++ Template generation is fully supported** for C# generic structs and methods, allowing the AOT transpiler to map C# generics directly to highly optimized C++ templates (`template <typename T>`). |
| **Expression-Bodied Members (`=>`)** | **Supported** | Transpiler automatically handles `void` and value-returning methods (injects `return` keyword). |
| **Default & Named Arguments** | **Supported** | Supported across device functions, local methods, and kernel launchers. Named arguments are normalized to declaration order, and omitted optional arguments are synthesized with compile-time default constant literals. |
| **Ternary Operator (`? :`)** | **Supported** | Standard conditional ternary operators are supported. |
| **Braceless `if` Statements** | **Supported** | Transpiler automatically injects `{ }` block scoping into the C++ source for single-statement control flow branches. |
| **Delegates & Lambda Expressions** | **Device Only (Partial)** | Non-capturing lambdas and local functions, or those capturing only value types, are extracted and expanded as C++ `__device__` functions (or lambdas). Complex closures requiring managed heap allocations (e.g., capturing reference types) are not supported. |
| **`switch` Expressions** | **Device Only (Partial)** | Only `ConstantPattern` (literals) and `DiscardPattern` (`_`) are supported. Using `when` clauses or complex pattern matching (like property patterns) will result in a compiler error (`UnsupportedSyntax`). |
| **`ref switch` Expressions** | **Unsupported by C#** | C# does not currently support `ref switch` expressions (`ref var r = x switch { ... }`). Use `if/else` or `ref ? :` for conditional `ref` assignment. |

## 5.2 Dialect Syntax Support

Both `GSharpCudaDialect` and `StandardCppDialect` fully support device synchronization and warp-level intrinsics:

* **Device Synchronization (`Gpu.SyncThreads()`, `Gpu.SyncGrid()`)**: Hardware thread block barriers (`__syncthreads()`) execute natively on CUDA. On the CPU backend (`StandardCppDialect`), barriers are lowered into a switch-based state machine (`StateMachineLowerer`) that synchronizes OpenMP threads across barrier steps without race conditions or deadlocks.
* **Warp Shuffles (`Warp.ShuffleSync`, `Warp.ActiveMask()`)**: Hardware warp intrinsics execute via PTX shuffle instructions on CUDA. On the CPU backend, `WarpInstructionRewriter` hoists warp operations so the CPU runtime emulates warp communication deterministically.

Per the [Semantics Charter](06_semantics.md), the CPU fallback is a reference implementation of the GPU model that executes the same barriers and warp steps as hardware SIMT execution.

## 5.3 Standard Diagnostics and Warnings

Because G# utilizes the standard Roslyn C# compiler to validate syntax before transpilation, certain standard C# rules must be carefully managed.

### 5.3.1 Suppressing Pointer Warnings
To manipulate raw memory arrays (e.g., `int*`), G# kernels must be marked with the `unsafe` keyword. Developers should configure their `.csproj` to allow unsafe blocks (`<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`).

### 5.3.2 Dereference Warnings (`CS8602`)
When utilizing C# 8.0+ Nullable Reference Types, passing raw pointers might trigger `CS8602` (Dereference of a possibly null reference).
Since all pointer validity checks are pushed to the AOT/Runtime phase, developers may need to suppress these warnings locally via `#pragma warning disable CS8602` or configure analyzer rules appropriately.

## 5.4 Custom G# Diagnostics

The `Kassyi.GSharp.Compiler` AOT pipeline enforces constraints natively during compilation by traversing the C# AST and reporting specific MSBuild diagnostics. Click any Diagnostic ID for detailed causes, solutions, and Before/After code examples:

| ID | Severity | Title | Condition |
| :--- | :--- | :--- | :--- |
| [**GSH0000**](../03_bcl_api/diagnostics/GSH0000.md) | Info | AOT Artifact Error | Triggered when writing an AOT artifact fails during the build process. |
| [**GSH0001**](../03_bcl_api/diagnostics/GSH0001.md) | Error | Object creation not supported in GPU kernels | Triggered when `new` or `new()` is used on anything other than `SharedMemory<T>` or unmanaged `struct` types. |
| [**GSH0002**](../03_bcl_api/diagnostics/GSH0002.md) | Error | String usage not supported in GPU kernels | Triggered when the `string` keyword is used inside a kernel. |
| [**GSH0003**](../03_bcl_api/diagnostics/GSH0003.md) | Error | Unsupported syntax in GPU kernel | Triggered when unknown/unsupported C# syntax nodes are encountered in device code. |
| [**GSH0004**](../03_bcl_api/diagnostics/GSH0004.md) | Error | GpuKernel return type must be void or blittable | Triggered when a `[GpuKernel]` method declares an unsupported return type. Only `void` and unmanaged blittable value types are supported. |
| [**GSH0005**](../03_bcl_api/diagnostics/GSH0005.md) | Error | GpuBigIntExpr must be assigned via .Value | Triggered when a `GpuBigIntExpr` is assigned to a local variable (`var x = a + b;`) without being resolved into `.Value`. |
| [**GSH0006**](../03_bcl_api/diagnostics/GSH0006.md) | Error | InlinePtx argument must be a string literal | Triggered when `Gpu.InlinePtx` receives a dynamic string instead of a compile-time string literal. |
| [**GSH0007**](../03_bcl_api/diagnostics/GSH0007.md) | Error | Foreach is not allowed in GPU kernels | Triggered when `foreach` is used on types allocating an `IEnumerator` (e.g. `IEnumerable<T>`). Arrays, `Span<T>`, and `[InlineArray]` are fully supported. |
| [**GSH0008**](../03_bcl_api/diagnostics/GSH0008.md) | Error | LINQ is not allowed in GPU kernels | Triggered when LINQ query syntax or methods are used in device code. |
| [**GSH0009**](../03_bcl_api/diagnostics/GSH0009.md) | Error | Implicit var with literal needs suffix | Triggered when `var` is initialized with an untyped bare literal (e.g., `var x = 0;`). |
| [**GSH0010**](../03_bcl_api/diagnostics/GSH0010.md) | Error | GSharp Prelude missing | Triggered when the AOT transpiler fails to locate the necessary C++ runtime prelude headers. |
| [**GSH0011**](../03_bcl_api/diagnostics/GSH0011.md) | Error | Macro lambda requires a parameter | Triggered when a macro lambda mapping does not declare expected parameters. |
| [**GSH0012**](../03_bcl_api/diagnostics/GSH0012.md) | Error | Macro arguments must be compile-time constants | Triggered when a macro argument cannot be folded into a constant value during transpilation. |
| [**GSH0013**](../03_bcl_api/diagnostics/GSH0013.md) | Warning | Excessive static shared memory usage | Triggered when a static `SharedMemory<T>` allocation exceeds 32,768 bytes, which can reduce warp occupancy. |
| [**GSH0014**](../03_bcl_api/diagnostics/GSH0014.md) | Warning | Large struct passed by value (Deprecated) | Triggered when a custom struct $\ge$ 32 bytes is passed by value. Advises passing by reference (`ref`) or pointer. (Deprecated, replaced by GSH0064) |
| [**GSH0015**](../03_bcl_api/diagnostics/GSH0015.md) | Warning | Auto-allocated array in loop | Triggered when an auto-allocated array return kernel is invoked from within a host CPU loop. |
| [**GSH0016**](../03_bcl_api/diagnostics/GSH0016.md) | Error | Unroll factor must be a non-negative compile-time constant | Triggered when `factor` in `Gpu.Hint.Unroll` is negative or non-constant. |
| [**GSH0017**](../03_bcl_api/diagnostics/GSH0017.md) | Error | Unroll hint must precede a loop | Triggered when `Gpu.Hint.Unroll` does not immediately precede a `for`, `while`, or `do-while` loop. |
| [**GSH0018**](../03_bcl_api/diagnostics/GSH0018.md) | Error | GPU intrinsic called from host | Triggered when a GPU-specific intrinsic is invoked from standard CPU host code. |
| [**GSH0019**](../03_bcl_api/diagnostics/GSH0019.md) | Error | Manual GetEnumerator calls are not supported | Triggered when explicit calls to `GetEnumerator()` are made in device code. |
| [**GSH0020**](../03_bcl_api/diagnostics/GSH0020.md) | Error | Missing IDisposable implementation | Triggered when a class holds an owned disposable GPU resource but does not implement `IDisposable`. |
| [**GSH0021**](../03_bcl_api/diagnostics/GSH0021.md) | Warning | Undisposed GPU resource (Deprecated) | Triggered when `Dispose()` fails to dispose all owned GPU resources. (Deprecated, integrated into GSH0022) |
| [**GSH0022**](../03_bcl_api/diagnostics/GSH0022.md) | Error | Undisposed GPU scope | Triggered when an `IGpuResourceScope` is not disposed. |
| [**GSH0023**](../03_bcl_api/diagnostics/GSH0023.md) | Error | Property getter side-effect (Deprecated) | Triggered when a property getter allocates a disposable GPU resource. (Deprecated) |
| [**GSH0024**](../03_bcl_api/diagnostics/GSH0024.md) | Error | Cast operator side-effect | Triggered when a cast operator allocates a disposable GPU resource. |
| [**GSH0025**](../03_bcl_api/diagnostics/GSH0025.md) | Error | Unguarded multi-allocation | Triggered when multiple allocations in a constructor are not guarded with try-catch. |
| [**GSH0026**](../03_bcl_api/diagnostics/GSH0026.md) | Error | Static disposable allocation | Triggered when a static field allocates a disposable GPU resource. |
| [**GSH0027**](../03_bcl_api/diagnostics/GSH0027.md) | Error | Disposing borrowed resource | Triggered when disposing a borrowed resource parameter. |
| [**GSH0028**](../03_bcl_api/diagnostics/GSH0028.md) | Error | Disposable resource in Default Interface Method | Triggered when Default Interface Methods attempt to manage disposable resources. |
| [**GSH0029**](../03_bcl_api/diagnostics/GSH0029.md) | Warning | Manual unmanaged allocation detected | Triggered when low-level manual unmanaged allocation (`Gpu.Unsafe.Malloc`) is detected. |
| [**GSH0030**](../03_bcl_api/diagnostics/GSH0030.md) | Error | Gpu.SyncGrid requires [GpuKernel(Cooperative = true)] | Triggered when `Gpu.SyncGrid()` is called in a non-cooperative kernel. |
| [**GSH0031**](../03_bcl_api/diagnostics/GSH0031.md) | Error | Direct invocation of [GpuKernel] is prohibited | Triggered when a `[GpuKernel]` method is invoked directly from host code instead of via its generated `Launch*` wrapper. |
| [**GSH0032**](../03_bcl_api/diagnostics/GSH0032.md) | Error | Unrolled loop bounds must be statically deterministic | Triggered when `Gpu.Hint.Unroll` is used on a loop whose bounds are not compile-time constants. |
| [**GSH0033**](../03_bcl_api/diagnostics/GSH0033.md) | Warning | Unrolled loop may be silently ignored | Triggered when an unrolled loop contains dynamic control flow (`break`, `return`, `goto`). |
| [**GSH0034**](../03_bcl_api/diagnostics/GSH0034.md) | Error | The 'params' modifier is prohibited in GPU device code | Triggered when the `params` keyword is used in a `[GpuKernel]` or `[GpuDeviceFunction]`. |
| [**GSH0035**](../03_bcl_api/diagnostics/GSH0035.md) | Error | Cannot call parallel-dependent processing from a single-thread context | Triggered when parallel intrinsics are called from a single-thread context. |
| [**GSH0036**](../03_bcl_api/diagnostics/GSH0036.md) | Error | GPU code type must be partial | Triggered when a type containing `[GpuKernel]` or `[GpuDeviceFunction]` is not declared `partial`. |
| [**GSH0037**](../03_bcl_api/diagnostics/GSH0037.md) | Error | Barrier in a lambda or local function that the CPU dialects do not split | Triggered when a barrier is used in a nested lambda that CPU dialects cannot split. |
| [**GSH0038**](../03_bcl_api/diagnostics/GSH0038.md) | Error | Exception handling is not supported in GPU kernels | Triggered when `try`, `catch`, `finally`, or `throw` is used in a kernel. |
| [**GSH0039**](../03_bcl_api/diagnostics/GSH0039.md) | Error | ref structs cannot be held across barrier synchronization | Triggered when a `ref struct` spans a barrier. |
| [**GSH0040**](../03_bcl_api/diagnostics/GSH0040.md) | Error | goto statements cannot cross barrier synchronization | Triggered when a `goto` statement jumps across a barrier. |
| [**GSH0041**](../03_bcl_api/diagnostics/GSH0041.md) | Error | Stack pointers cannot be held across barrier synchronization | Triggered when a stack pointer spans a barrier. |
| [**GSH0042**](../03_bcl_api/diagnostics/GSH0042.md) | Error | Gpu.SyncThreads() cannot be called from methods that are not GPU code | Triggered when `Gpu.SyncThreads()` is called outside GPU code. |
| [**GSH0043**](../03_bcl_api/diagnostics/GSH0043.md) | Error | Gpu.SyncThreads() cannot be called inside a fixed block | Triggered when a barrier is placed inside a `fixed` block. |
| [**GSH0045**](../03_bcl_api/diagnostics/GSH0045.md) | Error | Recursive local functions cannot contain Gpu.SyncThreads() | Triggered when a recursive local function contains a barrier. |
| [**GSH0046**](../03_bcl_api/diagnostics/GSH0046.md) | Error | async, await, and yield return are prohibited in GPU kernels | Triggered when `async`, `await`, or `yield return` is used in a kernel. |
| [**GSH0047**](../03_bcl_api/diagnostics/GSH0047.md) | Error | The lock statement is prohibited in GPU kernels | Triggered when a `lock` statement is used in a kernel. |
| [**GSH0048**](../03_bcl_api/diagnostics/GSH0048.md) | Error | Source Generator Internal Error | Triggered when the source generator encounters an internal failure. |
| [**GSH0049**](../03_bcl_api/diagnostics/GSH0049.md) | Error | [TakesOwnership] applied to non-IDisposable type | Triggered when `[TakesOwnership]` is applied to a parameter whose type does not implement `IDisposable`. |
| [**GSH0050**](../03_bcl_api/diagnostics/GSH0050.md) | Error | [TakesOwnership] is only allowed on constructor parameters | Triggered when `[TakesOwnership]` is used on a method parameter. |
| [**GSH0051**](../03_bcl_api/diagnostics/GSH0051.md) | Error | Cannot take ownership of a reference parameter | Triggered when `[TakesOwnership]` is applied to a `ref` or `out` parameter. |
| [**GSH0058**](../03_bcl_api/diagnostics/GSH0058.md) | Error | Two structs share one AotContexts name | Triggered when two structs collide on their generated AotContexts name. |
| [**GSH0059**](../03_bcl_api/diagnostics/GSH0059.md) | Error | Kernel overloads cannot be told apart by their launch wrappers | Triggered when overloaded kernels produce identical launch wrapper signatures. |
| [**GSH0060**](../03_bcl_api/diagnostics/GSH0060.md) | Error | User-defined operators are not supported in GPU code | Triggered when a user-defined operator is used in device code. |
| [**GSH0061**](../03_bcl_api/diagnostics/GSH0061.md) | Error | Device functions that reach a barrier cannot be recursive | Triggered when a recursive device function reaches a barrier. |
| [**GSH0062**](../03_bcl_api/diagnostics/GSH0062.md) | Error | A call that suspends at a barrier is not supported in this context | Triggered when a suspending call is made from an unsupported context. |
| [**GSH0063**](../03_bcl_api/diagnostics/GSH0063.md) | Warning | Shared memory declarations on one line share a buffer in managed execution | Triggered when multi-variable shared memory declarations share a buffer. |
| [**GSH0064**](../03_bcl_api/diagnostics/GSH0064.md) | Warning | Kernel parameters exceed the parameter space limit | Triggered when the total byte footprint of a kernel's parameters exceeds 4,096 bytes. |
| [**GSH0065**](../03_bcl_api/diagnostics/GSH0065.md) | Error | Non-const static fields and properties are not supported in GPU code | Triggered when non-const static state is referenced in device code. |
| [**GSH0066**](../03_bcl_api/diagnostics/GSH0066.md) | Error | Instance [GpuDeviceFunction] methods are not supported | Triggered when `[GpuDeviceFunction]` is declared on an instance method. |
| [**GSH0067**](../03_bcl_api/diagnostics/GSH0067.md) | Error | Disposable resource is created and discarded | Triggered when a disposable resource is allocated without being assigned or returned. |
| [**GSH0068**](../03_bcl_api/diagnostics/GSH0068.md) | Error | Disposable local is never disposed | Triggered when a disposable local variable is not disposed on all exit paths. |
| [**GSH0069**](../03_bcl_api/diagnostics/GSH0069.md) | Error | SharedMemory element type must not be disposable | Triggered when `SharedMemory<T>` uses an `IDisposable` element type. |
| [**GSH0070**](../03_bcl_api/diagnostics/GSH0070.md) | Error | Device iterator shape not supported | Triggered when a GPU iterator method has an unsupported return type, element type, parameter modifier, or declaration shape. |
| [**GSH0071**](../03_bcl_api/diagnostics/GSH0071.md) | Error | Iterator value must be enumerated in place | Triggered when an `IEnumerable<T>` iterator value is not consumed directly by `foreach` or stored in a single-assignment local variable. |
| [**GSH0072**](../03_bcl_api/diagnostics/GSH0072.md) | Error | Iterator body construct not supported | Triggered when `Gpu.SyncThreads()`, `SharedMemory<T>`, or an iterator local crossing a barrier is used in an iterator or its consuming `foreach`. |
| [**GSH0073**](../03_bcl_api/diagnostics/GSH0073.md) | Warning | Iterator expansion too large | Triggered when expanding an iterator at a consumer `foreach` duplicates the loop body more than 32 times. |
| [**GSH0074**](../03_bcl_api/diagnostics/GSH0074.md) | Error | CopyAsyncWaitGroup count must be a compile-time constant between 0 and 7 | Triggered when `CopyAsyncWaitGroup` count is out of range or not constant. |
| [**GSH0075**](../03_bcl_api/diagnostics/GSH0075.md) | Error | Gpu.CopyAsync requires a SharedMemory<T> destination and a global memory source | Triggered when `Gpu.CopyAsync` arguments are invalid. |
| [**GSH0076**](../03_bcl_api/diagnostics/GSH0076.md) | Error | Unsupported warp matrix fragment or operation | Triggered when WMMA matrix types or operations are unsupported. |
| [**GSH0077**](../03_bcl_api/diagnostics/GSH0077.md) | Warning | Dynamic SharedMemory<T> reached by only some threads or with a thread-dependent count | Triggered when dynamic shared memory allocation is divergent. |
| [**GSH0078**](../03_bcl_api/diagnostics/GSH0078.md) | Error | Struct cannot be laid out on the device | Triggered when struct layout rules are violated. |
| [**GSH0079**](../03_bcl_api/diagnostics/GSH0079.md) | Error | Struct field type has no device representation | Triggered when a struct field type is unsupported on the device. |
| [**GSH0080**](../03_bcl_api/diagnostics/GSH0080.md) | Warning | Struct array or returned struct is converted element by element | Triggered when deep marshaling converts struct elements individually. |
| [**GSH0081**](../03_bcl_api/diagnostics/GSH0081.md) | Warning | Struct array allocates the array field of each element | Triggered when deep marshaling allocates nested arrays per element. |
| [**GSH0083**](../03_bcl_api/diagnostics/GSH0083.md) | Warning | Barrier that only some threads may reach, depending on the launch or on run-time values | Triggered when a barrier may be reached by only some threads. |
| [**GSH0084**](../03_bcl_api/diagnostics/GSH0084.md) | Warning | Barrier after an exit that only some threads take | Triggered when a barrier follows a conditional thread exit. |
| [**GSH0088**](../03_bcl_api/diagnostics/GSH0088.md) | Error | Device code of another assembly is not exported | Triggered when cross-assembly device code is not marked with `[assembly: GpuDeviceExport]`. |
| [**GSH0089**](../03_bcl_api/diagnostics/GSH0089.md) | Error | Device code of another assembly cannot be used | Triggered when cross-assembly device code cannot be imported. |
| [**GSH0090**](../03_bcl_api/diagnostics/GSH0090.md) | Error | Device code of two assemblies has the same C++ name | Triggered when two assemblies declare colliding device code symbols. |
| [**GSH0091**](../03_bcl_api/diagnostics/GSH0091.md) | Error | Stack array outlives its scope | Triggered when a stack array reference escapes its enclosing scope. |
| [**GSH0095**](../03_bcl_api/diagnostics/GSH0095.md) | Error | Warp shuffle width must be 1, 2, 4, 8, 16 or 32 | Triggered when a warp shuffle width is not a valid power of two. |
| [**GSH0099**](../03_bcl_api/diagnostics/GSH0099.md) | Warning | An AsParallel() aggregate runs sequentially in each thread | Triggered when `AsParallel()` is used without parallel reduction. |
| [**GSH0100**](../03_bcl_api/diagnostics/GSH0100.md) | Warning | Every block reduces the whole sequence of an AsParallel() aggregate | Triggered when parallel aggregation is redundant across blocks. |
| [**GSH0102**](../03_bcl_api/diagnostics/GSH0102.md) | Error | Method is too deeply nested or too complex to compile | Triggered when expression or control flow depth exceeds compiler limits. |
| [**GSH0103**](../03_bcl_api/diagnostics/GSH0103.md) | Error | Error site ID collision in device error channel | Triggered when two error sites produce the same ID. |
| [**GSH0106**](../03_bcl_api/diagnostics/GSH0106.md) | Error | Kernel parameter name conflicts with a launch parameter | Triggered when a kernel parameter shares a name with a launch wrapper parameter. |
| [**GSH0107**](../03_bcl_api/diagnostics/GSH0107.md) | Error | A method without [GpuDeviceFunction] is called from GPU code | Triggered when unmarked code is invoked from device context. |
| [**SLQ0001**](../03_bcl_api/diagnostics/SLQ0001.md) | Error | StackLinq method chain is too deep | Triggered when a StackLinq chain exceeds the compiler depth limit. |
| [**RS0030**](../03_bcl_api/diagnostics/RS0030.md) | Error | Banned API in GPU kernel | Triggered via `BannedApiAnalyzers` when a banned API is used in GPU kernels or GPU intrinsics in host code. |

For individual guides, see the [Diagnostics Reference Catalog](../03_bcl_api/diagnostics/00_index.md).

## See also



### BCL API

- [BCL API and Hardware Intrinsics Reference](../03_bcl_api/00_index.md)
- [7. Diagnostics and Exceptions](../03_bcl_api/01_diagnostics_and_exceptions.md)

### BCL API > Diagnostics

- [Transpiler and Analyzer Diagnostics (GSHxxxx)](../03_bcl_api/diagnostics/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../04_guides/00_index.md)
- [The CPU to GPU Paradigm Shift: Anti-Patterns & Best Practices](../04_guides/06_antipatterns_and_best_practices.md)

