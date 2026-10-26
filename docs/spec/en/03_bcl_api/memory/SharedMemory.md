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

**Why it matters.** The CPU and managed dialects allocate shared memory at run time instead of reserving it statically. If they allocated by call site or by the order of allocations, a caller and its callee could receive the same buffer, or a device function called from two places could receive two buffers, and the same kernel would compute different results on the CPU and on the GPU. To keep the CUDA semantics, the compiler gives every declaration a **site key**: the 64-bit FNV-1a hash of the documentation comment ID of the declaring method and the ordinal of the declaration in that method. The CPU runtime looks the block's buffer up by that key alone; the managed runtime looks it up by that key and by the element type. The key is computed from the generic definition, so the instantiations of a generic device function share the buffer of a declaration on the CPU, and on CUDA when the declaration is dynamic (see [Generic device functions](#generic-device-functions)).

Under managed execution every declaration has a buffer of its own, which other declarations never move, so the `SharedMemory<T>` a thread already holds stays valid while other threads declare more shared memory. A declaration that is reached again with a larger size, for example a device function whose dynamic size differs between calls, grows its buffer and keeps its contents. On CUDA the same request succeeds only while the buffer is the last dynamic one placed (see [Growing a declaration](#growing-a-declaration)).

Managed execution runs a device function that never reaches a barrier as ordinary C#, without the compiler's rewrite. There the constructor identifies the declaration by its file, line and method name (`[CallerFilePath]`, `[CallerLineNumber]`, `[CallerMemberName]`), the only facts about the caller that C# passes to a constructor; it cannot see parameter types or the position within a line. Two `SharedMemory<T>` declarations of one method, or of overloads of one name, written on the same line therefore share a buffer in managed execution; the analyzer reports them as [GSH0063](../diagnostics/GSH0063.md). Write each declaration on its own line.

### Dynamic Shared Memory

A declaration whose count is a C# constant (`new SharedMemory<float>(256)`, or a `const` field) is **static**: on CUDA it becomes `__shared__ T name[N]`, its size is fixed at compile time, and [GSH0013](../diagnostics/GSH0013.md) warns above 32 KB. Any other count (a parameter, `BlockDimension.X`, a non-constant expression) makes the declaration **dynamic**. Dynamic declarations live in the dynamic shared memory of the launch, whose size the host passes as `sharedMemBytes`:

```csharp
// TileKernel declares two dynamic SharedMemory<float>(n) with n = 256: two 1024-byte regions, each starting at a
// multiple of 16 bytes
Kernels.LaunchTileKernel(data, n, blockDimX: 256, sharedMemBytes: 2 * 256 * sizeof(float));
```

#### Placement

On CUDA the compiler carves every dynamic declaration out of the one dynamic buffer of the launch. The rules of [One Buffer per Declaration per Block](#one-buffer-per-declaration-per-block) hold for dynamic declarations in every dialect (rule 1 below); rules 2 to 4 describe how CUDA places them:

1. **One region per declaration per block.** A declaration reached again (a second call of the device function, another caller, a loop, recursion) returns the same region.
2. **On CUDA, placed in the order a thread first reaches each declaration.** The first dynamic declaration a thread reaches starts at offset 0; each one it reaches later for the first time starts after the end of the previous one. The order is the run-time order, not the order in the source: a region in a device function is placed where the thread is when it first calls that function.
3. **On CUDA, aligned to 16 bytes.** Each region starts at a multiple of `max(alignof(T), 16)` bytes, and the dynamic buffer itself is 16-byte aligned, so `cp.async` (`Gpu.CopyAsync`) and 16-byte vector loads and stores are aligned for every element of a 16-byte type. Static declarations are not part of the dynamic buffer and do not count toward `sharedMemBytes`.
4. **Device functions.** A dynamic declaration in a `[GpuDeviceFunction]` is placed in the same buffer as those of the kernel that calls it, and never overlaps them. A device function called from two kernels is placed independently in each kernel's launch.

The other dialects place regions differently, so a layout computed for CUDA does not describe them:

- The CPU (native) runtime keeps one table per block: the first thread of the block to reach a declaration places its region at the end of the buffer, aligned to `alignof(T)` only. Static declarations are placed the same way, in the same 64 KB buffer, and count toward its limit of 64 KB and 64 declarations per block.
- Managed execution gives every declaration an array of its own; there is no shared buffer, no offset and no alignment beyond the element type.

On CUDA every thread of a kernel keeps a table of the regions, with one entry (slot) for each dynamic declaration the kernel can reach, numbered in the order of the site keys; the offsets in it are filled in the run-time order above. A device function that reaches a dynamic declaration receives the table as a hidden last parameter. Such a device function therefore cannot be passed as a delegate (a method group, as in `Func<int, int> f = Scratch;`) and cannot be reached from a member of a struct (a method, a computed property, an operator): the compiler reports [GSH0003](../diagnostics/GSH0003.md), in every dialect, because the analyzer checks the CUDA translation. Call it directly, from a kernel, another device function, or a local function or lambda written inside one of them.

#### Computing `sharedMemBytes`

Because of the alignment, the size is **not** the sum of `n * sizeof(T)`. Walk the dynamic declarations in the order a thread first reaches them:

```text
offset = 0
for each dynamic declaration, in the order first reached:
    offset = align_up(offset, max(alignof(T), 16))
    offset = offset + n * sizeof(T)
sharedMemBytes >= offset
```

For example, two declarations of 3 `int` each need **28** bytes, not 24: the first region takes bytes 0 to 11, and the second starts at 16 and ends at 28. A kernel that passed the packed sum (24) traps on CUDA. When every element type is aligned to 16 bytes or less, `Σ (n_i * sizeof(T_i) + 15)` is always enough. A declaration that is reached with several counts needs its largest count.

The same calculation in C#, for the host code that launches the kernel:

```csharp
static int AlignUp(int offset, int alignment) => (offset + alignment - 1) / alignment * alignment;

int n = 3;
int bytes = 0;
bytes = AlignUp(bytes, 16) + n * sizeof(int); // first declaration:  0 .. 12
bytes = AlignUp(bytes, 16) + n * sizeof(int); // second declaration: 16 .. 28
Console.WriteLine(bytes); // 28
```

#### Growing a declaration

A declaration reached again with a larger count grows its region:

- CUDA and the CPU: while the region is the last one placed, it grows in place and keeps its contents. A region first reached with a count of 0 starts at the current end of the buffer, so it can grow in place as long as no other region is placed after it.
- CUDA: when another region has been placed after it, the region cannot move (moving would need every thread of the block at the same point), so the launch traps.
- CPU: when another region has been placed after it, the runtime moves the region to the end of the buffer and copies its contents.
- Managed: the order does not matter. The region keeps its array when the array is large enough for the new count, and otherwise moves to a new array with its contents copied.

Declare a region that grows last, or declare it with its largest count first.

#### Run-time checks

| Condition | CUDA | CPU (native) | Managed |
| --- | --- | --- | --- |
| The regions end past `sharedMemBytes` | Trap | Not checked (`sharedMemBytes` is not read); above 64 KB or 64 declarations of one block the process ends (`std::abort`) | Not checked |
| A negative count | Trap | Not checked: the byte count wraps around, so the region may overlap the others or the process ends (`std::abort`) | No region; the first element access throws `IndexOutOfRangeException` |
| A count of 0 | Empty region | Empty region | No region; the first element access throws `IndexOutOfRangeException` |
| A larger count for a region that is not the last | Trap | Moved and copied | Grows; copied to a new array when the old one is too small |

On CUDA the check is `bytes > sharedMemBytes || offset + bytes > sharedMemBytes`, so a negative count, which arrives as at least 2^63 bytes, traps instead of wrapping. A trap surfaces as a `CudaException` from the launch, and it leaves the CUDA context of the process unusable: later CUDA calls in the same process fail too. Size `sharedMemBytes` from the calculation above, and keep counts non-negative.

#### Threads must reach dynamic declarations alike

The CUDA table of regions belongs to each **thread**: a thread places a region where its own table says the end of the buffer is, with the count it computed. Every thread of the block gets the same layout only when all threads reach the dynamic declarations in the same order and with the same counts. The CPU and managed runtimes keep one set of regions per block, set up by the first thread to reach each declaration, so a kernel that breaks this rule may also give different results in different dialects.

The analyzer reports [GSH0077](../diagnostics/GSH0077.md) (a warning) for a dynamic declaration, or a call to a device function that reaches one, under a thread-variant condition, and for a dynamic declaration whose count depends on the thread. Declare the shared memory before the branch, with a count every thread computes alike:

```csharp
[GpuKernel]
public static void HalfKernel(int[] output, int n)
{
    int tid = ThreadIndex.X;
    var half = new SharedMemory<int>(n);   // every thread reaches it, with the same n
    if (tid < 16)
    {
        half[tid] = tid;
        output[tid] = half[tid];
    }
}
```

#### Generic device functions

The site key, and so the CUDA slot, comes from the generic definition, not from an instantiation. Managed execution looks a region up by site key and element type, so it separates element types, not instantiations. For the instantiations of a generic device function (`Fill<int>` and `Fill<float>`) this gives:

| Declaration in the generic device function | CUDA | CPU (native) | Managed |
| --- | --- | --- | --- |
| Dynamic, element type is a type parameter (`SharedMemory<T>(n)`) | Shared by all instantiations | Shared | One region per element type |
| Dynamic, element type is not a type parameter (`SharedMemory<int>(n)`) | Shared | Shared | Shared |
| Static, element type is a type parameter (`SharedMemory<T>(256)`) | One array per instantiation (each template instantiation has its own `__shared__` array) | Shared | One region per element type |
| Static, element type is not a type parameter (`SharedMemory<int>(256)`) | One array per instantiation | Shared | Shared |

The dialects agree only on the dynamic declaration whose element type is not a type parameter, and even there the instantiations share one region. Do not declare dynamic `SharedMemory<T>` in a generic device function.

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
| **Kernel Parameters (`ref`/`out`/`in`)** | **Supported** | The host launcher passes kernel parameters again on every state machine resume, so `ref`/`out`/`in`/`ref readonly` and `ref struct` parameters (`Span<T>`, `ReadOnlySpan<T>`) work across barriers. |
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

