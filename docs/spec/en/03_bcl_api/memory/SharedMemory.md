---
title: "SharedMemory<T> Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Provides high-speed, on-chip block-local memory allocations."
---

## SharedMemory<T> Struct

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Provides high-speed, on-chip block-local memory allocations.

```csharp
public readonly ref struct SharedMemory<T> where T : unmanaged
```

## Constructors

| Name | Description |
| --- | --- |
| SharedMemory(int, string, int) | Allocates statically-sized shared memory. |

## Properties

| Name | Description |
| --- | --- |
| Item[int] | Gets or sets the element at the specified index. |

## SharedMemory(int, string, int)

Declares static or dynamic shared memory with the specified element capacity.

```csharp
public SharedMemory(int size, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0,
    [CallerMemberName] string callerMemberName = "");
```

#### Parameters

`size` _int_<br>
The number of elements allocated in shared memory.

`callerFilePath` _string_<br>
The file of the declaration, filled in by the compiler. Do not pass it.

`callerLineNumber` _int_<br>
The line of the declaration, filled in by the compiler. Do not pass it.

`callerMemberName` _string_<br>
The method of the declaration, filled in by the compiler. Do not pass it.

> [!NOTE]
> A second constructor, `SharedMemory(int size, ulong siteKey)`, is hidden from IntelliSense. The G# compiler uses it in the code it generates for managed execution; do not call it directly.

## Item[int]

Gets or sets the element at the specified zero-based index in shared memory.

```csharp
public T this[int index] { get; set; }
```

#### Parameters

`index` _int_<br>
The zero-based element offset.

#### Property Value

`T`<br>
The element at the specified index.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated natively on the host CPU outside of the runtime execution scope. Does not throw when executed via the G# CPU Emulation Context for testing.

## Examples

### Static Allocation

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU)

try {
    // Declares block-local shared memory cache
    var cache = new SharedMemory<float>(256);
    
    // Assigning elements evaluates index and throws on CPU
    cache[0] = 3.14f;
    Console.WriteLine($"Shared memory set. Value: {cache[0]}");
} catch (GpuOnlyException) {
    Console.WriteLine("SharedMemory<T> allocations and accesses can only be executed on the GPU.");
}
```

## Remarks

Maps to `__shared__` memory in CUDA. Shared memory is dramatically faster than global memory and acts as a user-managed L1 cache for the Thread Block.

### One Buffer per Declaration per Block

Each `new SharedMemory<T>(n)` expression in the source is **one buffer per thread block**, exactly like a CUDA `__shared__` variable, which has static storage. This holds in every dialect (CUDA, CPU, managed):

- Every thread of the block that reaches the declaration sees the same buffer.
- A `[GpuDeviceFunction]` that declares shared memory has **one** buffer for that declaration, however many places call it. Two calls of the function, from the same call site or from different ones, read and write the same memory.
- Different declarations never share memory, even when a kernel and the device function it calls each declare one.

```csharp
[GpuDeviceFunction]
public static int Exchange(int value)
{
    var box = new SharedMemory<int>(256);   // one buffer per block for this line, shared by all callers
    box[ThreadIndex.X] = value;
    Gpu.SyncThreads();
    return box[(ThreadIndex.X + 1) % BlockDimension.X];
}

[GpuKernel]
public static void ExchangeKernel(int[] data)
{
    var tile = new SharedMemory<int>(256);  // a separate buffer: another declaration
    tile[ThreadIndex.X] = data[ThreadIndex.X];
    data[ThreadIndex.X] = Exchange(tile[ThreadIndex.X]) + Exchange(1);  // both calls use the buffer of 'box'
}
```

**Why it matters.** The CPU and managed dialects allocate shared memory at run time instead of reserving it statically. If they allocated by call site or by the order of allocations, a caller and its callee could receive the same buffer, or a device function called from two places could receive two buffers, and the same kernel would compute different results on the CPU and on the GPU. To keep the CUDA semantics, the compiler gives every declaration a **site key**: the 64-bit FNV-1a hash of the documentation comment ID of the declaring method and the ordinal of the declaration in that method. The CPU and managed runtimes look the block's buffer up by that key and by the element type, so the instantiations of a generic device function do not share a buffer, as on CUDA.

Under managed execution every declaration has a buffer of its own, which other declarations never move, so the `SharedMemory<T>` a thread already holds stays valid while other threads declare more shared memory. A declaration that is reached again with a larger size, for example a device function whose dynamic size differs between calls, grows its buffer and keeps its contents, as a CUDA launch whose `sharedMemBytes` fits the largest call would.

Managed execution runs a device function that never reaches a barrier as ordinary C#, without the compiler's rewrite. There the constructor identifies the declaration by its file, line and method name (`[CallerFilePath]`, `[CallerLineNumber]`, `[CallerMemberName]`), the only facts about the caller that C# passes to a constructor; it cannot see parameter types or the position within a line. Two `SharedMemory<T>` declarations of one method, or of overloads of one name, written on the same line therefore share a buffer in managed execution; the analyzer reports them as [GSH0065](../diagnostics/GSH0065.md). Write each declaration on its own line.

### CPU Emulation (Testing)

When executing GPU kernels on the host CPU for testing purposes, `SharedMemory<T>` is fully emulated using host memory (`Span<T>` over `stackalloc` or pooled arrays). 

To accurately simulate block-scoped synchronization (`Gpu.SyncThreads()`) without the massive overhead of OS threads, the G# Source Generator transforms the kernel into an **AOT Flat State Machine**. This enables **Bulk Synchronous Parallel (BSP)** execution, distributing execution phases across the host's logical CPU cores.

#### Emulation Constraints & Support Matrix

Due to architectural differences between CUDA's SIMT hardware and C# CPU emulation (which relies on AOT State Machine lowering), several strict constraints apply. 
Violations are either statically blocked by Roslyn Analyzers at compile-time or explicitly marked as undefined behavior.

| Feature / Pattern | CPU Emulation Support | Diagnostic / Mitigation |
| :--- | :---: | :--- |
| **Uniform `SyncThreads()`** | **Fully Supported** | Standard patterns like tiling and reductions work identically to the GPU. |
| **`SyncThreads()` inside Loops** | **Supported (AST Lowering)** | `for`/`while` loops containing barriers are automatically lowered into flat `goto` structures by the G# Source Generator. |
| **Divergent Barriers** | **Strictly Prohibited** | `SyncThreads()` inside thread-dependent conditional branches (e.g., `if (threadIdx.X < 16)`) will trigger a Roslyn compile-time error ([GSH0037](../diagnostics/GSH0037.md)). |
| **Exception Handling** | **Strictly Prohibited** | `try-catch-finally` blocks inside kernels cannot cross barrier bounds and trigger a Roslyn compile-time error ([GSH0038](../diagnostics/GSH0038.md)). |
| **Cross-Barrier `ref struct`** | **Strictly Prohibited** | `ref` variables or `ref struct`s (e.g., `Span<T>`) cannot be held across barriers. Triggers a Roslyn compile-time error ([GSH0039](../diagnostics/GSH0039.md)). |
| **Cross-Barrier `goto`** | **Strictly Prohibited** | `goto` statements cannot jump across barrier bounds. Triggers a Roslyn compile-time error ([GSH0040](../diagnostics/GSH0040.md)). |
| **Cross-Barrier Local Pointer** | **Strictly Prohibited** | Pointers to local variables (`&x`) or `stackalloc` memory cannot be held across barriers. Triggers a Roslyn compile-time error ([GSH0041](../diagnostics/GSH0041.md)). |
| **External Method Barriers** | **Strictly Prohibited** | `Gpu.SyncThreads()` cannot be called inside external helper methods. It must be inside the `[GpuKernel]` method or its local functions. Triggers a Roslyn compile-time error ([GSH0042](../diagnostics/GSH0042.md)). |
| **Barriers inside `fixed` blocks** | **Strictly Prohibited** | Suspending inside a `fixed` block releases the GC pin, risking access violations. Triggers a Roslyn compile-time error ([GSH0043](../diagnostics/GSH0043.md)). |
| **Kernel Parameters (`ref`/`out`/`in`)** | **Strictly Prohibited** | `ref struct`s or `ref`/`out`/`in` parameters cannot be saved to the state machine context. Triggers a Roslyn compile-time error ([GSH0044](../diagnostics/GSH0044.md)). |
| **Recursive Barriers** | **Strictly Prohibited** | Recursive local functions containing barriers cause infinite AST inlining and are blocked. Triggers a Roslyn compile-time error ([GSH0045](../diagnostics/GSH0045.md)). |
| **State Machine Conflicts** | **Strictly Prohibited** | `async`, `await`, `yield`, and `lock` conflict with the G# state machine. Triggers Roslyn compile-time errors ([GSH0046](../diagnostics/GSH0046.md), [GSH0047](../diagnostics/GSH0047.md)). |
| **Implicit Warp Synchronization** | **Unsupported (Undefined)** | Relying on hardware lock-step execution without explicit barriers causes memory races on the CPU. Always use explicit barriers. |
| **Address Space Checks** | **Unsupported (Undefined)** | Emulation cannot detect if a shared memory pointer is erroneously passed as a global pointer. This will only fault on the actual GPU. |

#### Hardware-Level Emulation Limitations (Undefined Behaviors)

While the G# Source Generator ensures C# language-level safety and control flow fidelity, simulating the physical CUDA SIMT architecture on a standard CPU (.NET) introduces unavoidable hardware-level discrepancies. It is physically impossible to emulate all undefined behaviors. Representative limitations include:

1. **Memory Consistency Model (Data Race Concealment)**
   CPUs (e.g., x64) enforce a relatively strong memory model (like Total Store Order), while GPUs operate on a very Weak Memory Model. If a kernel contains data races (e.g., missing explicit memory fences `Gpu.ThreadFence()`), the CPU emulator may coincidentally execute it correctly, thereby concealing the bug. The bug will only manifest as data corruption or deadlocks when deployed to the actual GPU.
2. **Floating-Point (FP32) & FMA Precision**
   GPU hardware utilizes specialized arithmetic units (e.g., for Fast Math `__sinf`, `__expf`, and hardware FMA) which differ algorithmically from standard CPU `.NET` `MathF` libraries. Consequently, Golden Master tests comparing floating-point outputs between the CPU emulator and actual GPU execution will typically mismatch at the lowest bits (ULP differences).

## See also



### BCL API > Gpu Class

- [Gpu Class](../gpu_class/00_index.md)
- [Gpu.SyncThreads Method (G#)](../gpu_class/SyncThreads.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Warp Primitives and Thread Masking Guide](../../04_guides/01_warp_programming_guide.md)
- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../../04_guides/02_memory_safety_patterns.md)

