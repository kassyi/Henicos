---
title: "Transpiler and Analyzer Diagnostics (GSHxxxx)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Comprehensive catalog of compiler errors, warnings, and analyzer rules emitted by G#."
---

# Transpiler and Analyzer Diagnostics (GSHxxxx)

The G# compiler and Roslyn analyzers enforce GPU safety and memory management rules at compile-time. When a constraint is violated, the build system reports one of the following diagnostic codes.

## Diagnostic Codes

| ID | Severity | Title |
| :--- | :--- | :--- |
| [GSH0001](GSH0001.md) | Error | Object creation not supported in GPU kernels |
| [GSH0002](GSH0002.md) | Error | String usage not supported in GPU kernels |
| [GSH0003](GSH0003.md) | Error | Unsupported syntax in GPU kernel |
| [GSH0004](GSH0004.md) | Error | GpuKernel return type must be void or blittable |
| [GSH0005](GSH0005.md) | Error | GpuBigIntExpr must be assigned via .Value |
| [GSH0006](GSH0006.md) | Error | InlinePtx argument must be a string literal |
| [GSH0007](GSH0007.md) | Error | Foreach is not allowed in GPU kernels |
| [GSH0008](GSH0008.md) | Error | LINQ is not allowed in GPU kernels |
| [GSH0009](GSH0009.md) | Error | Implicit var with literal needs suffix |
| [GSH0010](GSH0010.md) | Error | GSharp Prelude missing |
| [GSH0011](GSH0011.md) | Error | Macro lambda requires a parameter |
| [GSH0012](GSH0012.md) | Error | Macro arguments must be compile-time constants |
| [GSH0013](GSH0013.md) | Warning | Excessive static shared memory usage |
| [GSH0014](GSH0014.md) | Warning | Large struct passed by value (Deprecated) |
| [GSH0015](GSH0015.md) | Warning | Auto-allocated array in loop |
| [GSH0016](GSH0016.md) | Error | Unroll factor must be a non-negative compile-time constant |
| [GSH0017](GSH0017.md) | Error | Unroll hint must precede a loop |
| [GSH0018](GSH0018.md) | Error | GPU intrinsic called from host |
| [GSH0019](GSH0019.md) | Error | Manual GetEnumerator calls are not supported |
| [GSH0020](GSH0020.md) | Error | Missing IDisposable implementation |
| [GSH0021](GSH0021.md) | Error | Undisposed GPU resource |
| [GSH0022](GSH0022.md) | Error | Unsafe field overwrite |
| [GSH0023](GSH0023.md) | Error | Property getter side-effect |
| [GSH0024](GSH0024.md) | Error | Cast operator side-effect |
| [GSH0025](GSH0025.md) | Error | Unguarded multi-allocation |
| [GSH0026](GSH0026.md) | Error | Static disposable allocation |
| [GSH0027](GSH0027.md) | Error | Disposing borrowed resource |
| [GSH0028](GSH0028.md) | Error | Disposable resource in Default Interface Method |
| [GSH0029](GSH0029.md) | Warning | Manual unmanaged allocation detected |
| [GSH0030](GSH0030.md) | Error | SyncGrid requires a cooperative kernel |
| [GSH0031](GSH0031.md) | Error | Direct invocation of [GpuKernel] is prohibited |
| [GSH0032](GSH0032.md) | Error | Unrolled loop bounds must be statically deterministic |
| [GSH0033](GSH0033.md) | Warning | Unrolled loop may be silently ignored |
| [GSH0034](GSH0034.md) | Error | The 'params' modifier is prohibited in GPU device code |
| [GSH0035](GSH0035.md) | Error | Cannot call parallel-dependent processing from a single-thread context |
| [GSH0036](GSH0036.md) | Error | Kernel class must be partial |
| [GSH0037](GSH0037.md) | Error | Divergent barriers are prohibited |
| [GSH0038](GSH0038.md) | Error | Exception handling is not supported in GPU kernels |
| [GSH0039](GSH0039.md) | Error | ref structs cannot be held across barrier synchronization |
| [GSH0040](GSH0040.md) | Error | goto statements cannot cross barrier synchronization |
| [GSH0041](GSH0041.md) | Error | Stack pointers cannot be held across barrier synchronization |
| [GSH0042](GSH0042.md) | Error | Gpu.SyncThreads() cannot be called from methods that are not GPU code |
| [GSH0043](GSH0043.md) | Error | Gpu.SyncThreads() cannot be called inside a fixed block |
| [GSH0045](GSH0045.md) | Error | Recursive local functions cannot contain Gpu.SyncThreads() |
| [GSH0046](GSH0046.md) | Error | async, await, and yield return are prohibited in GPU kernels |
| [GSH0047](GSH0047.md) | Error | The lock statement is prohibited in GPU kernels |
| [GSH0048](GSH0048.md) | Error | Source Generator Internal Error |
| [GSH0049](GSH0049.md) | Error | [TakesOwnership] applied to non-IDisposable type |
| [GSH0050](GSH0050.md) | Error | [TakesOwnership] is only allowed on constructor parameters |
| [GSH0051](GSH0051.md) | Error | Cannot take ownership of a reference parameter |
| [GSH0058](GSH0058.md) | Error | Two structs share one AotContexts name |
| [GSH0059](GSH0059.md) | Error | Kernel overloads cannot be told apart by their launch wrappers |
| [GSH0060](GSH0060.md) | Error | User-defined operators are not supported in GPU code |
| [GSH0061](GSH0061.md) | Error | Device functions that reach a barrier cannot be recursive |
| [GSH0062](GSH0062.md) | Error | A call that suspends at a barrier is not supported in this context |
| [GSH0063](GSH0063.md) | Warning | Shared memory declarations on one line share a buffer in managed execution |
| [GSH0064](GSH0064.md) | Warning | Kernel parameters exceed the parameter space limit |
| [GSH0065](GSH0065.md) | Error | Non-const static fields and properties are not supported in GPU code |
| [GSH0066](GSH0066.md) | Error | Instance [GpuDeviceFunction] methods are not supported |
| [GSH0067](GSH0067.md) | Error | Disposable resource is created and discarded |
| [GSH0068](GSH0068.md) | Error | Disposable local is never disposed |
| [GSH0069](GSH0069.md) | Error | SharedMemory element type must not be disposable |
| [GSH0070](GSH0070.md) | Error | Device iterator signature or declaration shape is not supported |
| [GSH0071](GSH0071.md) | Error | Iterator value must be consumed directly by `foreach` or stored in a single-assignment local |
| [GSH0072](GSH0072.md) | Error | Construct is not supported inside a GPU iterator or its consuming `foreach` |
| [GSH0073](GSH0073.md) | Warning | Expanding an iterator at `foreach` copies the loop body more than 32 times |
| [GSH0074](GSH0074.md) | Error | CopyAsyncWaitGroup count must be a compile-time constant between 0 and 7 |
| [GSH0075](GSH0075.md) | Error | Gpu.CopyAsync requires a SharedMemory<T> destination and a global memory source |
| [GSH0076](GSH0076.md) | Error | Unsupported warp matrix (WMMA) type or operation |
| [GSH0077](GSH0077.md) | Warning | Dynamic SharedMemory<T> reached by only some threads or with a thread-dependent count |
| [GSH0078](GSH0078.md) | Error | Struct cannot be laid out on the device |
| [GSH0079](GSH0079.md) | Error | Struct field type has no device representation |
| [GSH0080](GSH0080.md) | Warning | Struct array is converted element by element |
| [GSH0081](GSH0081.md) | Warning | Struct array allocates the array field of each element |
| [GSH0082](GSH0082.md) | Error | Struct cannot be passed to a CPU kernel yet |
| [GSH0088](GSH0088.md) | Error | Device code of another assembly is not exported |
| [GSH0089](GSH0089.md) | Error | Device code of another assembly cannot be used |
| [GSH0090](GSH0090.md) | Error | Device code of two assemblies has the same C++ name |
| [GSH0091](GSH0091.md) | Error | Stack array outlives its scope |
| [GSH0095](GSH0095.md) | Error | Warp shuffle width must be 1, 2, 4, 8, 16 or 32 |
| [SLQ0001](SLQ0001.md) | Warning | Excessive StackLinq chain depth |
| [RS0030](RS0030.md) | Error | GPU intrinsic invoked from host code or banned API used in kernel |

## See Also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [5. Limitations and Diagnostics](../../01_language/05_limitations.md)

### BCL API

- [BCL API and Hardware Intrinsics Reference](../00_index.md)
- [7. Diagnostics and Exceptions](../01_diagnostics_and_exceptions.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [The CPU to GPU Paradigm Shift: Anti-Patterns & Best Practices](../../04_guides/06_antipatterns_and_best_practices.md)
