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

| ID | Severity | Description & Resolution |
| :--- | :--- | :--- |
| [**GSH0001**](diagnostics/GSH0001.md) | Error | **Object creation not supported.** You attempted to use `new Class()`. Remove the allocation or use a `struct`. |
| [**GSH0002**](diagnostics/GSH0002.md) | Error | **String usage not supported.** You attempted to use the `string` type. |
| [**GSH0003**](diagnostics/GSH0003.md) | Error | **Unsupported syntax.** Triggered when unknown/unsupported C# syntax nodes are encountered. |
| [**GSH0004**](diagnostics/GSH0004.md) | Error | **Unsupported Return Type.** A `[GpuKernel]` method must return `void` or a supported value type. |
| [**GSH0005**](diagnostics/GSH0005.md) | Error | **GpuBigIntExpr Direct Assignment.** Triggered when a `GpuBigIntExpr` is assigned to a local variable without being resolved into `.Value`. Prevents silent "computation lost" bugs. |
| [**GSH0006**](diagnostics/GSH0006.md) | Error | **InlinePtx non-constant argument.** Triggered when `Gpu.InlinePtx` receives a dynamic string instead of a compile-time string literal. |
| [**GSH0007**](diagnostics/GSH0007.md) | Error | **foreach on unsupported collection.** Triggered when iterating over types requiring a heap enumerator (e.g. `IEnumerable<T>`). Arrays, `Span<T>`, and `[InlineArray]` are fully supported. |
| [**GSH0008**](diagnostics/GSH0008.md) | Error | **LINQ syntax not supported.** LINQ relies on delegates and dynamic allocations. Use imperative loops instead. |
| [**GSH0009**](diagnostics/GSH0009.md) | Error | **Implicit var requires explicit literal suffix.** Triggered when `var` is initialized with an untyped bare literal (e.g., `var x = 0;`), which can cause C++ `auto` type inference bugs. |
| [**GSH0010**](diagnostics/GSH0010.md) | Error | **GSharp Prelude missing.** Triggered when the AOT transpiler fails to locate the necessary C++ runtime prelude headers. |
| [**GSH0011**](diagnostics/GSH0011.md) | Error | **Macro lambda requires a parameter.** Triggered when a `[GpuOperatorMacro]` lambda mapping does not declare expected parameters. |
| [**GSH0012**](diagnostics/GSH0012.md) | Error | **Macro arguments must be compile-time constants.** Triggered when a macro argument cannot be folded into a constant value during transpilation. |
| [**GSH0013**](diagnostics/GSH0013.md) | Warning | **Excessive static shared memory.** A `SharedMemory<T>` allocation exceeds 32KB. This can severely reduce SM warp occupancy. |
| [**GSH0014**](diagnostics/GSH0014.md) | Warning | **Large struct passed by value (Deprecated).** A struct parameter exceeds 32 bytes. Pass it via `ref` or as an array to save limited constant memory space. (Deprecated, replaced by GSH0064) |
| [**GSH0064**](diagnostics/GSH0064.md) | Warning | **Kernel parameter space limit exceeded.** The total size of parameters passed to a kernel exceeds the 4 KB parameter space limit. Pass large structs using an array or buffer. |
| [**GSH0015**](diagnostics/GSH0015.md) | Warning | **Auto-allocate return in loop.** You called a kernel that returns an array from inside a `for` loop on the host. This allocates a new unmanaged buffer every iteration. |
| [**GSH0016**](diagnostics/GSH0016.md) | Error | **Unroll factor must be a non-negative compile-time constant.** The argument `factor` of `Gpu.Hint.Unroll` must be a compile-time non-negative integer. |
| [**GSH0017**](diagnostics/GSH0017.md) | Error | **Unroll hint must precede a loop.** `Gpu.Hint.Unroll` must immediately precede a `for`, `while`, or `do-while` loop. |
| [**GSH0018**](diagnostics/GSH0018.md) | Error | **GPU intrinsic called from host.** Triggered when a GPU-specific intrinsic is invoked from a CPU host method. |
| [**GSH0019**](diagnostics/GSH0019.md) | Error | **Manual GetEnumerator calls are not supported.** Explicit calls to `GetEnumerator()` are prohibited to ensure zero-allocation. Use a `foreach` loop over supported types like `Span<T>` instead. |
| [**GSH0020**](diagnostics/GSH0020.md) | Error | **Missing IDisposable implementation.** A class holds a disposable GPU resource (e.g., `GpuWorkspace`) but does not implement `IDisposable`. |
| [**GSH0021**](diagnostics/GSH0021.md) | Error | **Undisposed GPU Resource.** The `Dispose()` method does not explicitly invoke `.Dispose()` on all owned GPU resources. |
| [**GSH0022**](diagnostics/GSH0022.md) | Error | **Unsafe field overwrite.** A disposable field is overwritten without prior disposal. |
| [**GSH0023**](diagnostics/GSH0023.md) | Error | **Property getter side-effect.** Property getters must not allocate disposable resources. |
| [**GSH0024**](diagnostics/GSH0024.md) | Error | **Cast operator side-effect.** Implicit or explicit cast operators must not allocate disposable resources. |
| [**GSH0025**](diagnostics/GSH0025.md) | Error | **Unguarded multi-allocation.** Multiple allocations in a constructor must be guarded with try-catch to avoid partial leaks. |
| [**GSH0026**](diagnostics/GSH0026.md) | Error | **Static disposable allocation.** Static fields must not allocate disposable resources. |
| [**GSH0027**](diagnostics/GSH0027.md) | Error | **Disposing borrowed resource.** Disposing a borrowed resource creates a double-dispose hazard. If taking ownership, use `[TakesOwnership]`. |
| [**GSH0028**](diagnostics/GSH0028.md) | Error | **Disposable resource in Default Interface Method.** Default Interface Methods (DIM) cannot take ownership or dispose of resources. |
| [**GSH0029**](diagnostics/GSH0029.md) | Warning | **Manual unmanaged allocation detected.** You invoked manual unmanaged allocation (e.g., `Gpu.Unsafe.Malloc()`). Ensure corresponding `Free()` is called to prevent VRAM leaks. |
| [**GSH0030**](diagnostics/GSH0030.md) | Error | **SyncGrid requires a cooperative kernel.** Triggered when `Gpu.SyncGrid()` is called from a non-cooperative kernel. |
| [**GSH0031**](diagnostics/GSH0031.md) | Error | **Direct invocation of [GpuKernel] is prohibited.** You attempted to call a `[GpuKernel]` method directly from host code instead of using `Gpu.Launch()`. |
| [**GSH0032**](diagnostics/GSH0032.md) | Error | **Unrolled loop bounds must be statically deterministic.** The bounds of a loop marked with `Gpu.Hint.Unroll` cannot be determined at compile-time. |
| [**GSH0033**](diagnostics/GSH0033.md) | Warning | **Unrolled loop may be silently ignored.** A loop marked with `Gpu.Hint.Unroll` contains dynamic control flow (e.g., `break`), causing `nvcc` to ignore the hint. |
| [**GSH0034**](diagnostics/GSH0034.md) | Error | **The 'params' modifier is prohibited in GPU device code.** You used the `params` keyword in a `[GpuKernel]` or `[GpuDevice]` method, which incurs hidden heap allocation. |
| [**GSH0054**](diagnostics/GSH0054.md) | Warning | **InlinePtx not supported by dialect.** Triggered when `Gpu.InlinePtx` is transpiled for a dialect without the `InlineAssembly` capability (e.g. CPU); the call throws `NotSupportedException` at runtime. |
| [**GSH0055**](diagnostics/GSH0055.md) | Error | **Identifier containing `__` is reserved.** Triggered when an identifier declared in GPU code, or a member of a struct used there, contains a double underscore. `__` is reserved for G# internals (`__gsharp*`) and the C++ implementation. Rename it. |
| [**GSH0056**](diagnostics/GSH0056.md) | Error | **GPU method name is a C++ keyword (Deprecated).** No longer reported: method names are renamed automatically, so a method may be named after a C++ keyword. |
| [**GSH0057**](diagnostics/GSH0057.md) | Error | **Malformed trusted key line.** Triggered when a line in trusted_keys.txt is not a valid 64-character hex or 44-character Base64 key followed by a matching `,fnv1a64:<fingerprint>` suffix. |
| [**GSH0058**](diagnostics/GSH0058.md) | Error | **Two structs share one AotContexts name.** Two different structs used by the kernels of one class map to the same `AotContexts.{Name}` (`N1.Pair` and `N2.Pair`, `FooContext` and `Foo` because a trailing `Context` is dropped, or `Box<int>` and `Box<long>`). Rename one of the structs. |
| [**GSH0059**](diagnostics/GSH0059.md) | Error | **Kernel overloads cannot be told apart by their launch wrappers.** Two `[GpuKernel]` overloads map to the same `Launch{Kernel}` or `Launch{Kernel}Ptr` parameter list (for example `int[]` and `ref int`, which are both `GpuBuffer<int>`). Rename one of the kernels. |
| [**GSH0070**](diagnostics/GSH0070.md) | Error | **Device iterator shape not supported.** A GPU iterator method has an unsupported return type, element type, parameter modifier, or declaration shape. |
| [**GSH0071**](diagnostics/GSH0071.md) | Error | **Iterator value must be enumerated in place.** An `IEnumerable<T>` iterator value must be consumed directly by `foreach` or stored in a single-assignment local variable. |
| [**GSH0072**](diagnostics/GSH0072.md) | Error | **Iterator body construct not supported.** Barriers (`Gpu.SyncThreads()`), `SharedMemory<T>`, or an iterator local crossing a barrier are not permitted in an iterator or its consuming `foreach`. |
| [**GSH0073**](diagnostics/GSH0073.md) | Warning | **Iterator expansion too large.** Expanding an iterator at a consumer `foreach` duplicates the loop body more than 32 times. |
| [**GSH0074**](diagnostics/GSH0074.md) | Error | **CopyAsyncWaitGroup count must be a compile-time constant between 0 and 7.** The `pendingGroups` argument of `Gpu.CopyAsyncWaitGroup` must be a compile-time integer constant in `0..7`. |
| [**GSH0075**](diagnostics/GSH0075.md) | Error | **Gpu.CopyAsync requires a SharedMemory<T> destination and a global memory source.** The `sharedDst` argument must refer to an element of `SharedMemory<T>`, and `globalSrc` must refer to global memory (an element of an array, span or pointer parameter, a `ref` / `in` parameter, or a `ref` local bound to one) of the target type, without an implicit conversion. |
| [**GSH0076**](diagnostics/GSH0076.md) | Error | **Unsupported warp matrix (WMMA) type or operation.** A WMMA fragment or marker type, or a `Wmma.*` operation, uses an unsupported `(TShape, T)` profile, storage location, or memory operand. |
| [**GSH0077**](diagnostics/GSH0077.md) | Warning | **Dynamic SharedMemory<T> reached by only some threads or with a thread-dependent count.** A dynamic `SharedMemory<T>` declaration, or a call to a device function that reaches one, is under a thread-variant condition, or its count depends on the thread. On CUDA the threads of the block may then place the dynamic regions at different offsets. |
| [**GSH0078**](diagnostics/GSH0078.md) | Error | **Struct cannot be laid out on the device.** A kernel parameter or a device local uses a struct with partially overlapping or misaligned `[FieldOffset]` values, or with a reference-type field other than an array. |
| [**GSH0079**](diagnostics/GSH0079.md) | Error | **Struct field type has no device representation.** A struct used by a kernel parameter or a device local has a field whose type (`decimal`, `DateTime` ...) the dialect does not map. |
| [**GSH0080**](diagnostics/GSH0080.md) | Warning | **Struct array is converted element by element.** The host and device layouts of an array element struct differ, so the CUDA launcher converts each element through a shadow array. |
| [**GSH0081**](diagnostics/GSH0081.md) | Warning | **Struct array allocates the array field of each element.** Each element of a struct array parameter allocates and copies its array field separately (CUDA). |
| [**GSH0082**](diagnostics/GSH0082.md) | Error | **Struct cannot be passed to a CPU kernel yet.** A kernel parameter on the CPU dialect uses a struct whose host and device layouts differ (`LayoutKind.Auto`, array fields), which the CPU launcher cannot convert yet. |
| [**GSH0083**](diagnostics/GSH0083.md) | Warning | **Barrier that only some threads may reach, depending on the launch or on run-time values.** A barrier, or a call that reaches one, is behind a thread-variant branch that does not certainly diverge, such as `ThreadIndex.X < 16`; the certain forms are GSH0037. |
| [**GSH0088**](diagnostics/GSH0088.md) | Error | **Device code of another assembly is not exported.** GPU code calls a `[GpuDeviceFunction]`, or uses the constructor or computed property of a struct, of a referenced assembly that holds no `[assembly: GpuDeviceExport]` record for it. |
| [**GSH0089**](diagnostics/GSH0089.md) | Error | **Device code of another assembly cannot be used.** The record of a referenced assembly was written by another G# version, or its build could not transpile it for the consumer's dialect. |
| [**GSH0090**](diagnostics/GSH0090.md) | Error | **Device code of two assemblies has the same C++ name.** Two referenced assemblies, or a reference and the compilation, declare the same path, which becomes one C++ name. |
| [**SLQ0001**](diagnostics/SLQ0001.md) | Warning | **Excessive StackLinq chain depth.** Triggered when a StackLinq query chain exceeds recommended nesting depth, threatening GPU register spilling or stack frame bloat. |
| [**RS0030**](diagnostics/RS0030.md) | Error | **Banned API in GPU kernel.** Triggered when a banned .NET API is used in GPU kernels or GPU intrinsics are invoked from host CPU code. |

---

## 7.2 Runtime Exceptions

While device-side execution on the GPU fundamentally enforces a zero-allocation guarantee and prohibits dynamic exceptions, host-side execution and orchestration may throw runtime exceptions when encountering invalid configurations, CPU-side intrinsic misuse, or native driver failures.

### GpuOnlyException

- **Namespace:** `Kassyi.GSharp.Core`
- **Inherits from:** `System.InvalidOperationException`

Thrown when a GPU-only intrinsic, macro, execution context coordinate property, or device syntax marker (e.g., `Gpu.SyncThreads()`, `ThreadIndex.X`, `FastMath.Exp()`, `SharedMemory<T>`) is evaluated directly on the host CPU.

These members exist exclusively to provide C# type-safety, IntelliSense, and Roslyn syntax trees for AOT transpilation into CUDA C++. They possess no managed execution semantics on the CPU. Attempting to call them outside a transpiled kernel will trigger this exception immediately.

```csharp
try
{
    // Calling GPU intrinsics directly on CPU throws GpuOnlyException
    Gpu.SyncThreads();
}
catch (GpuOnlyException ex)
{
    Console.WriteLine($"Host execution blocked: {ex.Message}");
}
```

### CudaException

- **Namespace:** `Kassyi.GSharp.Runtime.Interop`
- **Inherits from:** `System.Exception`

Thrown when a native CUDA Driver API invocation fails (returns a `CUresult` other than `CUDA_SUCCESS` / `0`).

Common failure scenarios include:

- `CUDA_ERROR_OUT_OF_MEMORY` (2): Insufficient device VRAM during buffer allocation (`cuMemAlloc_v2`).
- `CUDA_ERROR_INVALID_VALUE` (1): Invalid grid or block launch dimensions, null pointers, or mismatched kernel parameter signatures.
- `CUDA_ERROR_NO_DEVICE` (100): No CUDA-capable GPU detected on the host system.
- `CUDA_ERROR_LAUNCH_FAILED` (700): An asynchronous device hardware exception occurred during kernel execution (e.g., out-of-bounds pointer dereference, misaligned memory access).

### NvrtcException

- **Namespace:** `Kassyi.GSharp.Runtime.Interop`
- **Inherits from:** `System.Exception`

Thrown when runtime JIT compilation via the NVIDIA Runtime Compilation (NVRTC) library fails. The exception message encapsulates the complete compilation log and error diagnostic output emitted by NVRTC.

---

## See also

### Language Specification

- [1. Language Specification](../01_language/00_index.md)
- [5. Limitations and Diagnostics](../01_language/05_limitations.md)

### BCL API > Diagnostics

- [Transpiler and Analyzer Diagnostics (GSHxxxx)](diagnostics/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../04_guides/00_index.md)
- [3. Exception Handling and Diagnostics Guide](../04_guides/03_exception_handling_guide.md)
