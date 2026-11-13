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
> To understand why standard exceptions (`throw new Exception()`) are prohibited on the GPU, how `GpuOnlyException` works, and how the G# analyzers enforce these constraints, please refer to the **[Exception Handling and Diagnostics Guide](../04_guides/03_exception_handling_guide.md)**.

---

## 7.1 Transpiler Diagnostic Codes (`GSHxxxx`)

During the build, the source generator of the `Kassyi.GSharp.Compiler` package (`GpuKernelGenerator`) and its analyzers examine your kernels and the host code that launches them. When they find syntax or types that cannot be transpiled, or host code that breaks a G# rule, they report `GSH` diagnostics (errors, warnings and, for GSH0000, information).

> [!TIP]
> For detailed Before/After code examples and remediation guidelines for each diagnostic, explore the [Diagnostics Reference Catalog](diagnostics/00_index.md).
> For language syntax constraints, refer to [1.5 Limitations](../01_language/05_limitations.md).

| ID | Severity | Description & Resolution |
| :--- | :--- | :--- |
| [**GSH0000**](diagnostics/GSH0000.md) | Info | **AOT Artifact Error.** Reported as information when the generator cannot write an ahead-of-time (AOT) artifact to the intermediate output directory. |
| [**GSH0001**](diagnostics/GSH0001.md) | Error | **Object creation not supported.** You attempted to use `new Class()`, or created an array outside the initializer of a local variable. Remove the allocation, use a `struct`, or use a local array or `stackalloc`. |
| [**GSH0002**](diagnostics/GSH0002.md) | Error | **String usage not supported.** You attempted to use the `string` type. |
| [**GSH0003**](diagnostics/GSH0003.md) | Error | **Unsupported syntax.** Triggered when unknown/unsupported C# syntax nodes are encountered. |
| [**GSH0004**](diagnostics/GSH0004.md) | Error | **Unsupported Return Type.** A `[GpuKernel]` method must return `void` or a supported value type. |
| [**GSH0005**](diagnostics/GSH0005.md) | Error | **GpuBigIntExpr Direct Assignment.** Triggered when a `GpuBigIntExpr` is assigned to a local variable without being resolved into `.Value`. Prevents silent "computation lost" bugs. |
| [**GSH0006**](diagnostics/GSH0006.md) | Error | **InlinePtx non-constant argument.** Triggered when `Gpu.InlinePtx` receives a dynamic string instead of a compile-time string literal. |
| [**GSH0007**](diagnostics/GSH0007.md) | Error | **foreach on unsupported collection.** Triggered when iterating over types requiring a heap enumerator (e.g. `IEnumerable<T>`). Arrays, `Span<T>`, and `[InlineArray]` are fully supported. |
| [**GSH0008**](diagnostics/GSH0008.md) | Error | **LINQ syntax not supported.** LINQ relies on delegates and dynamic allocations. Use imperative loops instead. |
| [**GSH0009**](diagnostics/GSH0009.md) | Error | **Implicit var requires explicit literal suffix.** Triggered when `var` is initialized with an untyped bare literal (e.g., `var x = 0;`), which can cause C++ `auto` type inference bugs. |
| [**GSH0010**](diagnostics/GSH0010.md) | Error | **GSharp Prelude missing.** Registered but never reported: the prelude is embedded in the compiler (scheduled for removal, #933). |
| [**GSH0011**](diagnostics/GSH0011.md) | Error | **Macro lambda requires a parameter.** Triggered when a `[GpuOperatorMacro]` lambda mapping does not declare expected parameters. |
| [**GSH0012**](diagnostics/GSH0012.md) | Error | **Macro arguments must be compile-time constants.** Triggered when a macro argument cannot be folded into a constant value during transpilation. |
| [**GSH0013**](diagnostics/GSH0013.md) | Warning | **Excessive static shared memory.** A `SharedMemory<T>` allocation exceeds 32KB. This can severely reduce SM warp occupancy. |
| [**GSH0014**](diagnostics/GSH0014.md) | Warning | **Large struct passed by value (Deprecated).** A struct parameter exceeds 32 bytes. Pass it via `ref` or as an array to save limited constant memory space. (Deprecated, replaced by GSH0064) |
| [**GSH0015**](diagnostics/GSH0015.md) | Warning | **Auto-allocate return in loop.** You called a kernel that returns an array from inside a loop (`for`, `foreach`, `while`, `do`) on the host. This allocates a new unmanaged buffer every iteration. |
| [**GSH0016**](diagnostics/GSH0016.md) | Error | **Unroll factor must be a non-negative compile-time constant.** The argument `factor` of `Gpu.Hint.Unroll` must be a compile-time non-negative integer. |
| [**GSH0017**](diagnostics/GSH0017.md) | Error | **Unroll hint must precede a loop.** `Gpu.Hint.Unroll` must immediately precede a `for`, `while`, or `do-while` loop. |
| [**GSH0018**](diagnostics/GSH0018.md) | Error | **GPU intrinsic called from host.** Triggered when host code calls, reads or names a GPU-only intrinsic (`Gpu.SyncThreads()`, `ThreadIndex.X`). |
| [**GSH0019**](diagnostics/GSH0019.md) | Error | **Manual GetEnumerator calls are not supported.** Explicit calls to `GetEnumerator()` are prohibited to ensure zero-allocation. Use a `foreach` loop over supported types like `Span<T>` instead. |
| [**GSH0020**](diagnostics/GSH0020.md) | Error | **Missing IDisposable implementation.** A class holds a disposable GPU resource (e.g., `GpuWorkspace`) but does not implement `IDisposable`. |
| [**GSH0021**](diagnostics/GSH0021.md) | Warning | **Undisposed GPU resource (Deprecated).** Disabled by default and no longer reported; an owned field that `Dispose()` does not release is reported as [GSH0022](diagnostics/GSH0022.md). |
| [**GSH0022**](diagnostics/GSH0022.md) | Error | **Undisposed GPU scope.** The Dispose() method of a type does not release an owned disposable field, directly or through a helper of the type. |
| [**GSH0023**](diagnostics/GSH0023.md) | Error | **Property getter side-effect.** Property getters must not allocate disposable resources. |
| [**GSH0024**](diagnostics/GSH0024.md) | Error | **Cast operator side-effect.** Implicit or explicit cast operators must not allocate disposable resources. |
| [**GSH0025**](diagnostics/GSH0025.md) | Error | **Unguarded multi-allocation.** Multiple allocations in a constructor must be guarded with try-catch to avoid partial leaks. |
| [**GSH0026**](diagnostics/GSH0026.md) | Error | **Static disposable allocation.** Static fields must not allocate disposable resources. |
| [**GSH0027**](diagnostics/GSH0027.md) | Error | **Disposing borrowed resource.** Disposing a borrowed resource creates a double-dispose hazard. If taking ownership, use `[TakesOwnership]`. |
| [**GSH0028**](diagnostics/GSH0028.md) | Error | **Disposable resource in Default Interface Method.** Default Interface Methods (DIM) cannot take ownership or dispose of resources. |
| [**GSH0029**](diagnostics/GSH0029.md) | Warning | **Manual unmanaged allocation detected.** You invoked manual unmanaged allocation (e.g., `Gpu.Unsafe.Malloc()`). Ensure corresponding `Free()` is called to prevent VRAM leaks. |
| [**GSH0030**](diagnostics/GSH0030.md) | Error | **SyncGrid requires a cooperative kernel.** Triggered when `Gpu.SyncGrid()` is called from a non-cooperative kernel. |
| [**GSH0031**](diagnostics/GSH0031.md) | Error | **Direct invocation of [GpuKernel] is prohibited.** You attempted to call a `[GpuKernel]` method directly from host code instead of its generated `Launch{Kernel}` method. |
| [**GSH0032**](diagnostics/GSH0032.md) | Error | **Unrolled loop bounds must be statically deterministic.** The bounds of a loop marked with `Gpu.Hint.Unroll` cannot be determined at compile-time. |
| [**GSH0033**](diagnostics/GSH0033.md) | Warning | **Unrolled loop may be silently ignored.** A loop marked with `Gpu.Hint.Unroll` contains dynamic control flow (e.g., `break`), causing `nvcc` to ignore the hint. |
| [**GSH0034**](diagnostics/GSH0034.md) | Error | **The 'params' modifier is prohibited in GPU device code.** You used the `params` keyword in a `[GpuKernel]` or `[GpuDeviceFunction]` method, which incurs hidden heap allocation. |
| [**GSH0035**](diagnostics/GSH0035.md) | Error | **Cannot call parallel-dependent processing from a single-thread context.** A method or operator depending on parallel execution was called from a single-thread context. |
| [**GSH0036**](diagnostics/GSH0036.md) | Error | **GPU code type must be partial.** A class containing methods annotated with the [GpuKernel] or [GpuDeviceFunction] attribute, or a type enclosing such a class, is not declared with the partial modifier. |
| [**GSH0037**](diagnostics/GSH0037.md) | Error | **Barrier in a lambda or local function that the CPU dialects do not split.** A barrier is written in a lambda or a local function whose body the CPU dialects do not split into the phases of the kernel, so it cannot wait for the other threads in any launch. |
| [**GSH0038**](diagnostics/GSH0038.md) | Error | **Exception handling is not supported in GPU kernels.** A `try`, `catch`, or `finally` block was detected inside a `[GpuKernel]` method. |
| [**GSH0039**](diagnostics/GSH0039.md) | Error | **ref structs cannot be held across barrier synchronization.** A `ref` variable or a `ref struct` (such as `Span<T>`) is used across a `Gpu.SyncThreads()` barrier, or a `ref struct` parameter changed before the barrier is used after it. |
| [**GSH0040**](diagnostics/GSH0040.md) | Error | **goto statements cannot cross barrier synchronization.** A `goto` statement jumps across a `Gpu.SyncThreads()` barrier or a call to a device function that may suspend. |
| [**GSH0041**](diagnostics/GSH0041.md) | Error | **Stack pointers cannot be held across barrier synchronization.** A pointer to a local variable (using the `&` operator) or a pointer obtained via `stackalloc` was held across a `Gpu.SyncThreads()` barrier. |
| [**GSH0042**](diagnostics/GSH0042.md) | Error | **Gpu.SyncThreads() cannot be called from methods that are not GPU code.** A call to `Gpu.SyncThreads()` was found inside a method that is neither a `[GpuKernel]` nor a `[GpuDeviceFunction]`. |
| [**GSH0043**](diagnostics/GSH0043.md) | Error | **Gpu.SyncThreads() cannot be called inside a fixed block.** A call to `Gpu.SyncThreads()` was found inside a `fixed` statement block. |
| [**GSH0045**](diagnostics/GSH0045.md) | Error | **Recursive local functions cannot contain Gpu.SyncThreads().** A local function contains a call to `Gpu.SyncThreads()` and is invoked recursively. |
| [**GSH0046**](diagnostics/GSH0046.md) | Error | **async, await, and yield return are prohibited in GPU kernels.** An `async` or `await` keyword was used in a GPU method, or `yield` was used inside a `[GpuKernel]` method. |
| [**GSH0047**](diagnostics/GSH0047.md) | Error | **The lock statement is prohibited in GPU kernels.** A `lock` statement was used inside a `[GpuKernel]` method. |
| [**GSH0048**](diagnostics/GSH0048.md) | Error | **Source Generator Internal Error.** An internal compiler error occurred in the G# Source Generator. |
| [**GSH0049**](diagnostics/GSH0049.md) | Error | **[TakesOwnership] applied to non-IDisposable type.** Triggered when the [TakesOwnership] attribute is applied to a parameter whose type does not implement IDisposable. |
| [**GSH0050**](diagnostics/GSH0050.md) | Error | **[TakesOwnership] is only allowed on constructor parameters.** Triggered when [TakesOwnership] attribute is applied to a parameter outside of a constructor declaration. |
| [**GSH0051**](diagnostics/GSH0051.md) | Error | **Cannot take ownership of a reference parameter.** Triggered when [TakesOwnership] attribute is applied to a parameter with a reference modifier (ref, in, out). |
| [**GSH0052**](diagnostics/GSH0052.md) | Error | **Native API definition is not valid YAML.** Triggered when a YAML native API definition marked with GSharpApi="true" cannot be parsed. |
| [**GSH0053**](diagnostics/GSH0053.md) | Error | **Native API definition violates the schema.** Triggered when a YAML native API definition is well-formed but does not satisfy the G# native API schema. |
| [**GSH0054**](diagnostics/GSH0054.md) | Warning | **Intrinsic not implemented by dialect.** Triggered when GPU code calls an intrinsic that the target dialect does not implement (`Gpu.InlinePtx`, mbarrier, WMMA or `Gpu.ConvertToSharedMemory` on the CPU dialect); the call throws `NotSupportedException` at runtime. |
| [**GSH0055**](diagnostics/GSH0055.md) | Error | **Identifier containing `__` is reserved.** Triggered when an identifier declared in GPU code, or a member of a struct used there, contains a double underscore. `__` is reserved for G# internals (`__gsharp*`) and the C++ implementation. Rename it. |
| [**GSH0056**](diagnostics/GSH0056.md) | Error | **GPU method name is a C++ keyword (Deprecated).** No longer reported: method names are renamed automatically, so a method may be named after a C++ keyword. |
| [**GSH0057**](diagnostics/GSH0057.md) | Error | **Malformed trusted key line.** Triggered when a line in trusted_keys.txt is not a valid 64-character hex or 44-character Base64 key followed by a matching `,fnv1a64:<fingerprint>` suffix. |
| [**GSH0058**](diagnostics/GSH0058.md) | Error | **Two structs share one AotContexts name.** Two different structs used by the kernels of one class map to the same `AotContexts.{Name}` (`N1.Pair` and `N2.Pair`, `FooContext` and `Foo` because a trailing `Context` is dropped, or `Box<int>` and `Box<long>`). Rename one of the structs. |
| [**GSH0059**](diagnostics/GSH0059.md) | Error | **Kernel overloads cannot be told apart by their launch wrappers.** Two `[GpuKernel]` overloads map to the same `Launch{Kernel}` or `Launch{Kernel}Ptr` parameter list (for example `int[]` and `ref int`, which are both `GpuBuffer<int>`). Rename one of the kernels. |
| [**GSH0060**](diagnostics/GSH0060.md) | Error | **User-defined operators are not supported in GPU code.** Triggered when a user-defined operator without a device definition (any type, `System.Numerics` included, unless the operator has `[GpuOperatorMacro]`) is invoked inside GPU device code. |
| [**GSH0061**](diagnostics/GSH0061.md) | Error | **Device functions that reach a barrier cannot be recursive.** A [GpuDeviceFunction] reaches Gpu.SyncThreads() or a warp shuffle and calls itself, directly or through other device functions. |
| [**GSH0062**](diagnostics/GSH0062.md) | Error | **A call that suspends at a barrier is not supported in this context.** A warp shuffle, vote or reduction, Gpu.SyncWarp, Warp.ActiveMask(), or a [GpuDeviceFunction] that reaches a barrier, is called from a lambda, a local function, a switch expression arm, a 'when' clause, after '?.' or as the right operand of '??' (a warp shuffle, vote or reduction also in a using or fixed statement); or such a device function is called with a by-reference argument that a call, a property or a ref conditional selects. |
| [**GSH0063**](diagnostics/GSH0063.md) | Warning | **Shared memory declarations on one line share a buffer in managed execution.** Two SharedMemory<T> declarations of the same method, or of overloads with the same name, are written on the same line. |
| [**GSH0064**](diagnostics/GSH0064.md) | Warning | **Kernel parameter space limit exceeded.** The total size of parameters passed to a kernel exceeds the 4 KB parameter space limit. Pass large structs using an array or buffer. |
| [**GSH0065**](diagnostics/GSH0065.md) | Error | **Non-const static fields and properties are not supported in GPU code.** A kernel or device function reads a non-const static field (e.g., `static readonly int`) or a static property. Use a `const` field or pass the value as a parameter. |
| [**GSH0066**](diagnostics/GSH0066.md) | Error | **Instance [GpuDeviceFunction] methods are not supported.** Instance methods marked with [GpuDeviceFunction] are not supported. |
| [**GSH0067**](diagnostics/GSH0067.md) | Error | **Disposable resource is created and discarded.** A disposable resource is created by an expression statement and never bound to a variable. |
| [**GSH0068**](diagnostics/GSH0068.md) | Error | **Disposable local is never disposed.** A local that owns a disposable resource leaves its scope without being disposed. |
| [**GSH0069**](diagnostics/GSH0069.md) | Error | **SharedMemory element type must not be disposable.** SharedMemory<T> was created with an element type that implements IDisposable. |
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
| [**GSH0080**](diagnostics/GSH0080.md) | Warning | **Struct array or returned struct is converted element by element.** The host and device layouts of an array element struct, or of a struct a kernel returns, differ, so the launcher converts each element through a shadow array. |
| [**GSH0081**](diagnostics/GSH0081.md) | Warning | **Struct array allocates the array field of each element.** Each element of a struct array parameter allocates and copies its array field separately (CUDA). |
| [**GSH0083**](diagnostics/GSH0083.md) | Warning | **Barrier that only some threads may reach, depending on the launch or on run-time values.** A barrier, or a call that reaches one, is behind a thread-variant branch (an `if`, a loop, `&&`, `\|\|`, `?:`, or a callee whose barrier diverges), and the threads that skip it reach another barrier. |
| [**GSH0084**](diagnostics/GSH0084.md) | Warning | **Barrier after an exit that only some threads take.** A thread-variant condition sends some threads out of the kernel (an early `return`, a `break`, the end of a branch) before a barrier that the others still reach. |
| [**GSH0088**](diagnostics/GSH0088.md) | Error | **Device code of another assembly is not exported.** GPU code calls a `[GpuDeviceFunction]`, or uses the constructor or computed property of a struct, of a referenced assembly that holds no `[assembly: GpuDeviceExport]` record for it. |
| [**GSH0089**](diagnostics/GSH0089.md) | Error | **Device code of another assembly cannot be used.** The record of a referenced assembly was written by another G# version, or its build could not transpile it for the consumer's dialect. |
| [**GSH0090**](diagnostics/GSH0090.md) | Error | **Device code of two assemblies has the same C++ name.** Two referenced assemblies, or a reference and the compilation, declare the same path, which becomes one C++ name. |
| [**GSH0091**](diagnostics/GSH0091.md) | Error | **Stack array outlives its scope.** A local array, the stack array of a span or the storage of a `stackalloc` is reassigned, returned, or stored where it would outlive the C++ array that holds it (a variable of an outer scope, a parameter, memory the method did not create), directly, through another local or through a call with a `ref` / `out` argument. |
| [**GSH0095**](diagnostics/GSH0095.md) | Error | **Warp shuffle width must be 1, 2, 4, 8, 16 or 32.** Triggered when a warp shuffle is called with a constant width that is not 1, 2, 4, 8, 16 or 32. |
| [**GSH0099**](diagnostics/GSH0099.md) | Warning | **An AsParallel() aggregate runs sequentially in each thread.** The threads of a block cannot share it (a thread-dependent branch or input, a private source, an order-dependent stage, a lambda with side effects, or a float / double sum); every thread still receives the whole sum. |
| [**GSH0100**](diagnostics/GSH0100.md) | Warning | **Every block reduces the whole sequence of an AsParallel() aggregate.** The inputs are the same in the whole grid, but the kernel is not cooperative, so the blocks cannot share the work; mark it `[GpuKernel(Cooperative = true)]`. |
| [**GSH0102**](diagnostics/GSH0102.md) | Error | **Method is too deeply nested or too complex to compile.** Triggered when an expression or control flow structure within a kernel or device function exhausts compiler execution stack space during transpilation. |
| [**GSH0103**](diagnostics/GSH0103.md) | Error | **Error site ID collision in device error channel.** Triggered when two distinct error sites produce the same 31-bit FNV-1a site ID within a compiled module. |
| [**GSH0106**](diagnostics/GSH0106.md) | Error | **Kernel parameter name conflicts with a launch parameter.** Triggered when a [GpuKernel] method declares a parameter with the same name as one of the launch option parameters of the generated host wrapper. |
| [**GSH0107**](diagnostics/GSH0107.md) | Error | **A method without [GpuDeviceFunction] is called from GPU code.** Triggered when a kernel or device function calls a method of the project that is not compiled for the GPU: a method without [GpuDeviceFunction], or an instance method of a struct. |
| [**GSH0108**](diagnostics/GSH0108.md) | Error | **Struct field type is not accessible to the generated launcher.** A struct used by a kernel parameter or return value has a field whose type (a private type nested in the struct ...) the kernel's class cannot name. |
| [**GSH0109**](diagnostics/GSH0109.md) | Error | **A method without a device implementation is called from GPU code.** Triggered when a kernel or device function calls a method of another assembly (the BCL or a library) that the target dialect has no implementation for. |
| [**GSH0110**](diagnostics/GSH0110.md) | Error | **The launcher of a [CudaImport] method cannot pass a parameter to the kernel.** A parameter of a `[CudaImport]` method has a type the generated launcher has no faithful way to pass: a reference type, a struct with `LayoutKind.Auto` (a tuple, `DateTime`), a ref struct, `Nullable<T>`, a `ref` / `out` / `in` parameter, a multidimensional or jagged array, or a launch setting name (`gridDimX`, `stream`, ...) with the wrong type. |
| [**GSH0111**](diagnostics/GSH0111.md) | Error | **[GpuKernel] is only allowed on an ordinary static method of a class.** A kernel runs only through its generated launcher, so `[GpuKernel]` on an operator, an accessor, a lambda, a local function, an instance method, a member of a struct, interface or generic type, a generic method, the entry point, or a method that the runtime or C# calls on its own is an error, and nothing is generated for it. |
| [**GSH0112**](diagnostics/GSH0112.md) | Error | **Kernel parameter type has no device representation.** A `[GpuKernel]` parameter (the receiver of an extension kernel included) is a reference to a managed object (a class, `string`, an interface, a delegate or `object`) or an array or span of them, which the launcher cannot copy to the device. |
| [**GSH0113**](diagnostics/GSH0113.md) | Error | **G# cannot generate the launcher of a [CudaImport] declaration.** `[CudaImport]` is on an `extern` method, a method with a body or a written implementing part, a generic method, a method that returns a value, or a method of a type that is not `partial` or is file-local; nothing is generated for it. |
| [**GSH0114**](diagnostics/GSH0114.md) | Warning | **G# has no launch limits for a GSharpTargetArchitectures architecture.** `GSharpTargetArchitectures` names an architecture whose compute capability is not in G#'s table of launch limits; the limits of the target GPUs are taken from the other architectures only. |
| [**SLQ0001**](diagnostics/SLQ0001.md) | Error | **StackLinq method chain is too deep.** A StackLinq query chain is longer than the compiler limit (`MaxChainDepth` = 50). Split the chain or materialize an intermediate result. |
| [**RS0030**](diagnostics/RS0030.md) | Error | **Banned API (G# repository builds only).** Reported by `BannedApiAnalyzers` for an API listed in the repository's `BannedSymbols.txt` files. A project that uses G# does not get it; there LINQ in a kernel is [GSH0008](diagnostics/GSH0008.md) and a GPU intrinsic on the host is [GSH0018](diagnostics/GSH0018.md). |

---

## 7.2 Runtime Exceptions

While device-side execution on the GPU fundamentally enforces a zero-allocation guarantee and prohibits dynamic exceptions, host-side execution and orchestration may throw runtime exceptions when encountering invalid configurations, CPU-side intrinsic misuse, or native driver failures.

### GpuOnlyException

- **Namespace:** `Kassyi.GSharp.Core`
- **Inherits from:** `System.InvalidOperationException`

Thrown when a GPU-only intrinsic or execution context coordinate property (e.g., `Gpu.SyncThreads()`, `ThreadIndex.X`) is evaluated on the host CPU outside a kernel launch.

These members are transpiled into CUDA C++ when they appear in a kernel. When a kernel runs on the managed CPU path, `ManagedGpuContext.IsActive` is `true` on its threads and the members return the values of the simulated thread. Called from ordinary host code, where no launch is active, they throw this exception immediately.

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

The class has two static helpers for the bodies of GPU-only members:

| Member | Description |
| :--- | :--- |
| `static void Throw()` | Throws a new `GpuOnlyException`. Use it as the body of a `void` member. |
| `static T Throw<T>()` | Throws a new `GpuOnlyException`; the return type lets it stand where an expression of type `T` is expected (`public static int Lane => GpuOnlyException.Throw<int>();`). It never returns. |

### CSharpExecutionUnsupportedException

- **Namespace:** `Kassyi.GSharp.Core`
- **Inherits from:** `System.InvalidOperationException`

Thrown when a kernel launched under [`KernelExecution.CSharp`](execution_context/GpuExecution.md) cannot run one GPU thread at a time: it reaches a barrier, a warp operation, shared memory, or code whose meaning only the generated code gives (an operator macro, a floating-point StackLinq `Sum`). The launcher throws it before any thread runs when the compiler finds such an operation in the kernel or the device functions it calls; an operation reached through a delegate throws it when a thread reaches it. Launch the kernel under `KernelExecution.Default` or `KernelExecution.Managed` instead.

```csharp
try
{
    using (GpuExecution.Use(KernelExecution.CSharp))
    {
        Kernels.LaunchReduceKernel(data, blockDimX: 256); // the kernel calls Gpu.SyncThreads()
    }
}
catch (CSharpExecutionUnsupportedException ex)
{
    Console.WriteLine(ex.Message); // names the kernel and the operation
}
```

### CudaException

- **Namespace:** `Kassyi.GSharp.Runtime.Interop`
- **Inherits from:** `System.Exception`

Thrown when a native CUDA Driver API invocation fails (returns a `CUresult` other than `CUDA_SUCCESS` / `0`).

Common failure scenarios include (the `CudaException.Result` value of type `CUresult`):

- `CudaErrorOutOfMemory` (2): Insufficient device VRAM during buffer allocation (`cuMemAlloc_v2`).
- `CudaErrorInvalidValue` (1): Invalid grid or block launch dimensions, null pointers, or mismatched kernel parameter signatures.
- `CudaErrorNoDevice` (100): No CUDA-capable GPU detected on the host system.
- `CudaErrorIllegalAddress` (700): The kernel accessed an invalid device address (e.g., an out-of-bounds pointer dereference).
- `CudaErrorLaunchFailed` (719): An unspecified device exception occurred during kernel execution.

When a kernel fault comes with an error that G# device code recorded before it stopped, the launcher throws the matching .NET exception instead (see below), with the `CudaException` as its inner exception.

### Exceptions raised by device code

G# does not compile kernels at run time; they are compiled when the project is built. A check that fails in device code (an index out of range, a division by zero, a `checked` overflow, a null reference) records an error code and its source location, and the launcher throws the matching .NET exception on the host: `IndexOutOfRangeException`, `DivideByZeroException`, `OverflowException`, `NullReferenceException`, `ArgumentException`, `ArgumentOutOfRangeException`, `NotSupportedException` or `InvalidOperationException` (`GSharpErrorMapper` in `Kassyi.GSharp.Runtime.Aot`). See the [Exception Handling and Diagnostics Guide](../04_guides/03_exception_handling_guide.md).

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
