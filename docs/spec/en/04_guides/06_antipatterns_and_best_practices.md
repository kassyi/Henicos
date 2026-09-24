# The CPU to GPU Paradigm Shift: Anti-Patterns & Best Practices

GSharp provides an incredibly powerful capability: writing C# code that executes natively on NVIDIA GPUs. However, beneath the familiar C# syntax lies a fundamentally different hardware architecture.

A developer approaching GPU programming with a pure CPU-centric mental model will almost certainly encounter catastrophic performance cliffs, unexpected deadlocks, or compiler rejections.

This guide outlines the six most common traps C# developers fall into when writing GSharp code, why they happen, and the best practices to overcome them.

---

## 1. Warp Divergence (The `if-else` Trap)

### ❌ CPU Intuition (The Trap)

On a CPU, each thread operates completely independently. You can write complex `if-else` blocks or `switch` statements, and the CPU's branch predictor will ensure that each thread takes only the path it needs. It feels natural to branch logic aggressively.

### ⚠️ GPU Reality (The Pitfall)

GPUs use a **SIMT (Single Instruction, Multiple Threads)** architecture. Threads are grouped into "warps" (typically 32 threads). All 32 threads in a warp **must execute the exact same instruction at the exact same time**.
If threads within a warp evaluate a condition differently (some take `if`, some take `else`), the GPU cannot execute them simultaneously. Instead, it must execute the `if` path while masking (idling) the `else` threads, and then execute the `else` path while masking the `if` threads.
This is called **Warp Divergence**. It completely serializes your code and can easily halve your compute throughput.

### ✅ Best Practice in G#

Minimize divergent branches within kernels.

- Prefer mathematical operations or bitwise masking over `if-else` logic where possible.
- If branching is unavoidable, ensure that all threads within the _same warp_ make the same decision (e.g., branching based on `ThreadIndex.Y` if threads are grouped logically).

> [!TIP]
> **Compiler Optimizations & Predication**
> For very simple assignments like `if (a > b) c = x; else c = y;`, the PTX compiler (NVVM) is smart enough to automatically optimize this into predicated instructions (e.g., `selp`), eliminating actual divergence. However, the compiler cannot optimize away divergence for complex logic, different memory access patterns, or loops with varying iterations.

```csharp
// Execution Context: G# Kernel (GPU)

// ❌ Bad: Complex Divergent Logic (Cannot be optimized to predication)
if (isComplexMode) {
    // 16 threads do heavy math and global memory accesses
    result = RunHeavyCalculations(data, id);
} else {
    // The other 16 threads finish instantly, but MUST WAIT
    // for the first 16 threads to complete their heavy math.
    result = 0.0f;
}

// ✅ Good: Branchless Math (Data-driven computation)
// Execution Context: G# Kernel (GPU)
// By converting the boolean to a 1.0f or 0.0f multiplier, all threads
// execute the exact same instructions without divergence.
float mask = isComplexMode ? 1.0f : 0.0f; // Optimized to selp
result = RunHeavyCalculations(data, id) * mask;
```

---

## 2. Memory Coalescing (AoS vs. SoA)

### ❌ CPU Intuition (The Trap)

Object-Oriented Programming (OOP) teaches us to group related data into cohesive objects or structures. If you are simulating a particle system, you might create a `struct Particle { float X; float Y; float Z; }` and pass an array of `Particle[]` to your processing loop. This is known as an **Array of Structures (AoS)**.

### ⚠️ GPU Reality (The Pitfall)

GPU Global Memory has immense bandwidth, but it only achieves maximum throughput when adjacent threads in a warp access adjacent memory addresses simultaneously—a process called **Memory Coalescing**.
If thread 0 reads `Particle.X`, thread 1 reads the next `Particle.X`, and so on, they are jumping over `Y` and `Z` in memory. This results in **uncoalesced memory access**, causing the GPU to fetch unnecessary data into the cache, wasting massive amounts of bandwidth and stalling execution.

### ✅ Best Practice in G#

Adopt Data-Oriented Design. Use a **Structure of Arrays (SoA)** instead.
Instead of an array of objects, pass individual arrays for each property.

```csharp
// Execution Context: G# Kernel (GPU)

// ❌ Bad (AoS): Uncoalesced access
public struct Particle { public float X, Y, Z; }
[GpuKernel]
public static void ProcessParticles(Particle[] particles) { /* ... */ }

// ✅ Good (SoA): Perfectly coalesced access
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void ProcessParticles(float[] posX, float[] posY, float[] posZ) { /* ... */ }
```

---

## 3. Zero-Allocation (The `new` Keyword Taboo)

### ❌ CPU Intuition (The Trap)

In C# (.NET), instantiating objects with `new MyClass()` is the foundation of development. The Garbage Collector (GC) works magically in the background to clean up short-lived objects.

### ⚠️ GPU Reality (The Pitfall)

**There is no Garbage Collector on the GPU.** Dynamic heap allocations during kernel execution are prohibitively expensive and architecturally hostile to thousands of concurrent threads.
For this reason, GSharp strictly enforces a **Zero-Allocation Paradigm** within GPU kernels. Attempting to allocate reference types (`class`) or use LINQ inside a `[GpuKernel]` will result in an immediate compile-time error.

### ✅ Best Practice in G#

- Pass all required memory buffers from the CPU (Host) to the GPU (Device) _before_ launching the kernel.
- Inside the kernel, rely entirely on value types (`struct`) and stack/register-based allocations.
- If you need temporary memory shared between threads, allocate and use `SharedMemory<T>`.

```csharp
// Execution Context: G# Kernel (GPU)

// Define types explicitly
public class MyClass { public int Value; }
public struct MyStruct { public int Value; }

// ❌ Bad: Heap Allocation
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void Process() {
    var obj = new MyClass(); // Compiler Error: Cannot allocate reference types on GPU
    obj.Value = 42;
}

// ✅ Good: Value Types & Stack Allocation
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void Process() {
    var obj = new MyStruct(); // Safe: Allocated purely on registers/local stack
    obj.Value = 42;
}
```

---

## 4. Synchronization & Deadlocks (The `lock` Trap)

### ❌ CPU Intuition (The Trap)

When multiple threads need to modify a shared variable, C# developers reach for `lock (obj)`, `Mutex`, or write manual `while (!Monitor.TryEnter(...))` spin-locks. If a thread can't get the lock, it yields or spins until the lock is released.

### ⚠️ GPU Reality (The Pitfall)

First, the C# `lock (obj)` keyword translates to `Monitor.Enter`, which requires reference types and OS-level thread management. Attempting to use `lock` inside a `[GpuKernel]` will result in a **compile-time error**.

When developers realize this, they often try to build a manual spin-lock using `Interlocked` operations. However, because of the SIMT architecture (see Warp Divergence), if Thread 0 acquires a spin-lock, and Thread 1 (in the same warp) tries to acquire it, Thread 1 will spin.
Because Thread 1 is spinning, the instruction pointer for the _entire warp_ is stuck on the spin loop. Thread 0 will **never** be allowed to execute the unlock instruction. This results in an immediate, unrecoverable **Deadlock**.

### ✅ Best Practice in G#

- **Never use locks.**
- Use `Gpu.AtomicAdd` operations (which map to hardware atomic instructions like `atomicAdd` or `atomicCAS`) for un-contended shared modifications.
- For block-level synchronization, use `Gpu.SyncThreads()` to ensure all threads reach a safe barrier.
- When dealing with warp-level synchronization (like `WarpShuffle`), ensure all active threads in the warp participate to prevent divergence deadlocks.

```csharp
// Execution Context: G# Kernel (GPU)

// ❌ Bad: The `lock` keyword (Compile Error)
[GpuKernel]
public static void ProcessWithLock(object syncRoot, ref int sharedTotal, int localValue) {
    // Compiler Error: Monitor.Enter and reference types are not supported on the GPU.
    lock (syncRoot) {
        sharedTotal += localValue;
    }
}

// ❌ Bad: Manual Spin-locks (Deadlock Hazard)
[GpuKernel]
public static void ProcessWithSpinLock(ref int lockValue, ref int sharedTotal, int localValue) {
    // A spinning thread prevents the lock-holder in the same warp from making progress!
    while (Interlocked.CompareExchange(ref lockValue, 1, 0) != 0) { } // Deadlock hazard

    sharedTotal += localValue; // Critical section

    Interlocked.Exchange(ref lockValue, 0); // Unlock
}

// ✅ Good: Data-centric Atomics (No locks needed)
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void ProcessWithAtomics(ref int sharedTotal, int localValue) {
    // Instead of locking a mutex to safely update, use hardware atomics directly on the data.
    // G# maps standard .NET Interlocked directly to PTX atomic instructions.
    Gpu.AtomicAdd(ref sharedTotal, localValue);
}
```

### Atomics vs. SyncThreads: What's the Difference?

When moving away from CPU `lock` paradigms, it is crucial to understand the distinct purposes of the two primary GPU synchronization primitives:

- **Data Protection (`Interlocked` / `Gpu.AtomicAdd`)**: Use this when multiple threads need to safely update the **same memory address** simultaneously (e.g., incrementing a shared counter or a histogram bin). It ensures the data is not corrupted, but it does _not_ control the order in which threads execute. Think of it as the GPU equivalent of `lock` or `Interlocked`.
- **Execution Barrier (`Gpu.SyncThreads()`)**: Use this when you need to synchronize **time (execution order)** across all threads in a block. It acts as a barrier where all threads must wait until everyone has arrived. It does _not_ prevent concurrent memory writes. Think of it as the GPU equivalent of `Task.WaitAll()` or a `Barrier`. Use it to prevent Read-After-Write (RAW) hazards, such as waiting for all threads to finish writing to `SharedMemory` before any thread is allowed to read from it.

```csharp
// Execution Context: G# Kernel (GPU)

// ✅ Good: Appropriate use of Synchronization Primitives
[GpuKernel]
public static void SyncVsAtomicExample(int[] data, int[] histogram) {
    int globalIdx = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
    int localValue = data[globalIdx];

    // -------------------------------------------------------------
    // 1. Data Protection: Atomics
    // -------------------------------------------------------------
    // Multiple threads might process the same 'localValue' and try
    // to increment the same histogram bin simultaneously.
    // Atomics ensure the bin count is safely incremented without locks.
    Gpu.AtomicAdd(ref histogram[localValue], 1);

    // -------------------------------------------------------------
    // 2. Execution Barrier: SyncThreads
    // -------------------------------------------------------------
    var sharedCache = SharedMemory<int>.Allocate(256);
    sharedCache[ThreadIndex.X] = localValue;

    // ⚠️ BARRIER: We must wait for ALL threads to finish writing to
    // 'sharedCache' before ANY thread is allowed to read from it.
    Gpu.SyncThreads();

    // Now it is completely safe to read from indices written by
    // OTHER threads in the same block.
    int reversedValue = sharedCache[255 - ThreadIndex.X];
}
```

---

## 5. The PCIe Bottleneck (Micro-transfers)

### ❌ CPU Intuition (The Trap)

If you have a complex pipeline, it feels natural to keep the orchestration on the CPU. You might loop through an array on the CPU, sending tiny chunks of data to the GPU, launching a quick kernel, and reading the result back immediately.

### ⚠️ GPU Reality (The Pitfall)

The GPU compute cores are incredibly fast, but the PCIe bus connecting the CPU and GPU is relatively slow and has high latency. If you send small amounts of data back and forth, your application will spend 99% of its time waiting for data transfers and 1% of its time actually computing.

### ✅ Best Practice in G#

**Kernel Fusion & Batching.**

- Send all your data to the GPU in one massive block.
- Chain your compute operations. If Kernel A produces data needed by Kernel B, do not read it back to the CPU. Leave it in GPU memory and immediately launch Kernel B.
- Only transfer data back to the CPU when the final, fully processed result is ready for display or disk I/O.

```csharp
// Execution Context: Host (CPU)

// ❌ Bad: Micro-transfers (Chatty API)
// Execution Context: Host (CPU)
public void ExecuteChattyPipeline(float[] hostData) {
    using var deviceBuffer = new GpuBuffer<float>(1000);
    for (int i = 0; i < 1000; i++) {
        // Uploading a single float per iteration incurs huge PCIe latency
        float[] singleElement = new[] { hostData[i] };
        deviceBuffer.CopyToDevice(singleElement);

        // Launching a kernel for a tiny payload wastes GPU compute potential
        LaunchProcessItemKernel(deviceBuffer, i, gridDimX: 1, blockDimX: 1);
    }
}

// ✅ Good: Kernel Fusion & Batching
// Execution Context: Host (CPU)
public void ExecuteBatchedPipeline(float[] hostData) {
    // Transfer the entire payload in one large, efficient PCIe block transfer
    using var deviceBuffer = new GpuBuffer<float>(hostData.Length);
    deviceBuffer.CopyToDevice(hostData);

    // Compute heavily on the GPU in a single massive parallel launch
    LaunchProcessBatchKernel(deviceBuffer, gridDimX: (uint)((hostData.Length + 255) / 256), blockDimX: 256);
}
```

---

## 6. Print Debugging (The `Console.WriteLine` Trap)

### ❌ CPU Intuition (The Trap)

When a CPU calculation produces unexpected results, the first reflex for most C# developers is to sprinkle `Console.WriteLine($"debug: x={x}");` across methods to observe runtime variable states and inspect control flow.

### ⚠️ GPU Reality (The Pitfall)

NVIDIA GPU cores do not have standard I/O pipes, consoles, or text streams attached to them. Furthermore, because thousands of GPU threads execute concurrently, attempting to output text synchronously from each thread would completely stall SIMT execution units. Consequently, `Console.WriteLine` and standard .NET I/O streams are completely prohibited inside a `[GpuKernel]`.

> [!NOTE]
> **What happens if you call `Console.WriteLine` or unsupported library methods?**
>
> If you attempt to invoke `Console.WriteLine`—or **any unmapped third-party library or BCL method**—inside a `[GpuKernel]`, G# will reject the code immediately at **build time (compile time)**. It does *not* wait until runtime to crash on the GPU:
>
> - **[GSH0003](../03_bcl_api/diagnostics/GSH0003.md) (Unsupported syntax in GPU kernel)**: Triggered because the method is not mapped to a native CUDA/PTX intrinsic. Any unfamiliar method invocation is rejected.
> - **[GSH0002](../03_bcl_api/diagnostics/GSH0002.md) (String usage not supported in GPU kernels)**: Triggered if string literals or interpolated strings are passed as arguments.
> - **[RS0030](../03_bcl_api/diagnostics/RS0030.md) (Banned API in GPU kernel)**: Triggered by Roslyn analyzers for prohibited standard .NET runtime APIs.
>
> This enforcement adheres to G#'s **Static Determinism** principle: all code must be fully resolved at compile time, guaranteeing that invalid API calls are highlighted with red squiggles in your IDE before code ever touches the GPU.

### ✅ Best Practice in G#

**Telemetry-Driven Buffer Logging.**

Allocate a dedicated, pre-allocated debug array on the Host CPU and pass it to the GPU kernel. Inside the kernel, record telemetry values and variable states into the buffer using thread-safe hardware atomics (`Gpu.AtomicAdd`). Once the kernel finishes, inspect the recorded values on the Host CPU.

#### 1. Device Kernel Implementation

```csharp
// Execution Context: G# Kernel (Device/GPU)
using Kassyi.GSharp.Core;

public static class DebugExampleKernels
{
    [GpuKernel]
    public static void ProcessWithDebugLog(
        int[] input, 
        int[] output, 
        int[] debugBuffer, 
        int[] debugIndex)
    {
        int tid = ThreadIndex.X;
        int myValue = input[tid] * 2;
        output[tid] = myValue;

        // Thread-safe logging to a pre-allocated debug array
        int idx = Gpu.AtomicAdd(ref debugIndex[0], 1);
        if (idx < debugBuffer.Length)
        {
            debugBuffer[idx] = myValue;
        }
    }
}
```

#### 2. Host Orchestration & Verification

```csharp
// Execution Context: Host (CPU)
using System;

// 1. Allocate input, output, and telemetry debug buffers
var input = new int[] { 10, 20, 30, 40 };
var output = new int[input.Length];
var debugBuffer = new int[64];
var debugIndex = new int[1]; // Atomic counter initialized to 0

// 2. Launch kernel (using auto-generated Launch method)
DebugExampleKernels.LaunchProcessWithDebugLog(
    input, output, debugBuffer, debugIndex, 
    gridDimX: 1, blockDimX: input.Length
);

// 3. Inspect the logged values on the CPU
int loggedCount = Math.Min(debugIndex[0], debugBuffer.Length);
for (int i = 0; i < loggedCount; i++)
{
    Console.WriteLine($"[GPU Log {i}] value = {debugBuffer[i]}");
}
```

## See also




### Language Specification

- [1. Language Specification](../01_language/00_index.md)
- [5. Limitations and Diagnostics](../01_language/05_limitations.md)

### Guides

- [4. Practical Guides and Best Practices](00_index.md)
- [5. Precision and Performance Guide](05_precision_and_performance_guide.md)
- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](02_memory_safety_patterns.md)

