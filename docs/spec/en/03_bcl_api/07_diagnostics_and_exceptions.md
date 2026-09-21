# 7. Diagnostics and Exceptions

The G# compilation and execution pipelines impose strict boundaries between host CPU code and device GPU kernels. This document details how exceptions are handled (or prohibited) and catalogs the diagnostic errors emitted by the G# Roslyn analyzer and transpiler.

---

## 7.1 The "No Exceptions" Rule

NVIDIA GPUs and the CUDA execution model do not support standard C++ or C# exception unwinding (e.g., `try`, `catch`, `finally`, `throw`).

If a hardware fault occurs (such as an illegal memory access or an out-of-bounds array read), the GPU hardware generates a trap, which halts the kernel and returns a failure code (`CUDA_ERROR_ILLEGAL_ADDRESS`) to the host. The host `.NET` runtime will then throw a standard `CudaException`.

> [!IMPORTANT]
> **You cannot write `throw new Exception()` inside a G# kernel.** 
> Doing so requires managed heap allocation (for the Exception object itself) and relies on runtime type information (RTTI) and stack unwinding, none of which exist on the GPU device.

---

## 7.2 `GpuOnlyException`

You will notice that all intrinsic methods in `Kassyi.GSharp.Core` (e.g., `Gpu.SyncThreads()`, `ThreadIndex.X`) throw a `GpuOnlyException`.

```csharp
public static int X => GpuOnlyException.Throw<int>();
```

**Why does this exist if exceptions are prohibited?**
These methods are designed to be intercepted and replaced by the G# AOT Transpiler *at compile-time*. 
If you accidentally call `ThreadIndex.X` from standard CPU code (e.g., inside `Main()`), the transpiler never intercepts it. Thus, the real C# method executes, immediately throwing the `GpuOnlyException` to prevent undefined CPU behavior.

---

## 7.3 Analyzer Enforcement (`RS0030`)

To prevent the usage of inherently incompatible .NET libraries inside GPU kernels, G# utilizes the `Microsoft.CodeAnalysis.BannedApiAnalyzers` package.

For example, attempting to use standard LINQ extension methods (which heavily rely on dynamic memory allocation and delegates) inside a `[GpuKernel]` will trigger an immediate compiler error:

**Error RS0030: The API 'System.Linq.Enumerable' is banned in this project.**

---

## 7.4 Transpiler Diagnostic Codes (`GSHxxxx`)

During the MSBuild AOT code generation phase, the `Kassyi.Generators.GSharp` Roslyn Source Generator analyzes the AST of your kernels. If it encounters syntax or types that cannot be transpiled to CUDA, it emits custom `GSH` diagnostic errors.

| ID | Severity | Description & Resolution |
| :--- | :--- | :--- |
| **GSH0001** | Error | **Object creation not supported.** You attempted to use `new Class()`. Remove the allocation or use a `struct`. |
| **GSH0002** | Error | **String usage not supported.** You attempted to use the `string` type. |
| **GSH0003** | Error | **Unsupported syntax.** Triggered when unknown/unsupported C# syntax nodes are encountered. |
| **GSH0004** | Error | **Unsupported Return Type.** A `[GpuKernel]` method must return `void` or a supported value type. |
| **GSH0005** | Error | **GpuBigIntExpr Direct Assignment.** Triggered when a `GpuBigIntExpr` is assigned to a local variable without being resolved into `.Value`. Prevents silent "computation lost" bugs. |
| **GSH0006** | Error | **InlinePtx non-constant argument.** Triggered when `Gpu.InlinePtx` receives a dynamic string instead of a compile-time string literal. |
| **GSH0007** | Error | **foreach loop not supported.** `foreach` allocates an `IEnumerator` on the heap. Use a standard `for` loop, or iterate over a `Span<T>`. |
| **GSH0008** | Error | **LINQ syntax not supported.** LINQ relies on delegates and dynamic allocations. Use imperative loops instead. |
| **GSH0009** | Error | **Implicit var requires explicit literal suffix.** Triggered when `var` is initialized with an untyped bare literal (e.g., `var x = 0;`), which can cause C++ `auto` type inference bugs. |
| **GSH0010** | Error | **GSharp Prelude missing.** Triggered when the AOT transpiler fails to locate the necessary C++ runtime prelude headers. |
| **GSH0011** | Error | **Macro lambda requires a parameter.** Triggered when a `[GpuOperatorMacro]` lambda mapping does not declare expected parameters. |
| **GSH0012** | Error | **Macro arguments must be compile-time constants.** Triggered when a macro argument cannot be folded into a constant value during transpilation. |
| **GSH0013** | Warning | **Excessive static shared memory.** A `SharedMemory<T>` allocation exceeds 32KB. This can severely reduce SM warp occupancy. |
| **GSH0014** | Warning | **Large struct passed by value.** A struct parameter exceeds 32 bytes. Pass it via `ref` or as an array to save limited constant memory space. |
| **GSH0015** | Warning | **Auto-allocate return in loop.** You called a kernel that returns an array from inside a `for` loop on the host. This allocates a new unmanaged buffer every iteration. |
| **GSH0016** | Error | **CudaCpp Dialect Not Active.** Triggered when a CUDA-specific hardware intrinsic is invoked while the `CpuFallback` dialect is active. |
| **GSH021** | Error | **Missing IDisposable implementation.** A class holds a disposable GPU resource (e.g., `GpuBuffer<T>`) but does not implement `IDisposable`. |
| **GSH022** | Error | **Undisposed GPU Resource.** The `Dispose()` method does not explicitly invoke `.Dispose()` on all owned GPU resources. |
| **GSW010** | Warning | **Manual unmanaged allocation detected.** You invoked `Gpu.Unsafe.Malloc()`. Ensure corresponding `Free()` is called to prevent VRAM leaks. |

> [!TIP]
> For a complete mapping of supported vs unsupported standard C# syntax, refer to [1.5 Limitations](../01_language/05_limitations.md).
