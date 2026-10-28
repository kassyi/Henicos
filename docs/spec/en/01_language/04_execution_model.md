---
title: "4. Execution and Concurrency Model"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "G# is designed strictly for Massively Parallel Processor (MPP) architectures, mapping C# constructs directly to NVIDIA's CUDA execution model. Unde..."
---

# 4. Execution and Concurrency Model

G# is designed strictly for Massively Parallel Processor (MPP) architectures, mapping C# constructs directly to NVIDIA's CUDA execution model. Understanding this model is critical for writing correct and performant G# code.

## 4.1 The Thread Hierarchy

G# kernels are executed by an array of threads. These threads are organized into a two-level hierarchy:

1. **Thread Block:** A group of threads that execute concurrently on a single Streaming Multiprocessor (SM). Threads within the same block can cooperate via **Shared Memory** and synchronize their execution using **Barrier Synchronization**.
2. **Grid:** An array of Thread Blocks that execute the same kernel. Blocks within a Grid are executed independently and can be scheduled in any order across the available SMs.

### 4.1.1 Indexing

To determine their unique identity and workload, threads access built-in execution indices.
_(For detailed information on indices such as `ThreadIndex` and `BlockIndex`, refer to the BCL reference [Execution Context](../03_bcl_api/execution_context/00_index.md))._

## 4.2 Warp Execution Model

At the hardware level, Thread Blocks are partitioned into **Warps** (groups of 32 threads in current NVIDIA architectures). A warp executes one common instruction at a time.

### 4.2.1 Warp Divergence

When threads within the same warp take different execution paths due to data-dependent conditional branching (`if`/`else`), the warp **diverges**.

- **Behavior:** The hardware executes each branch path serially, disabling threads that are not on that path. When all paths complete, the threads converge back to the same execution path.

> Warp divergence severely degrades performance. G# developers should avoid branching on condition variables that vary across threads within a warp (e.g., `ThreadIndex.X`). Condition variables based on `BlockIndex.X` or `[Constant]` parameters do not cause divergence within a block.

### 4.2.2 Warp Intrinsics (Synchronization and Shuffle)

G# natively supports hardware Warp-level intrinsics, allowing threads within the same warp to communicate and synchronize efficiently without relying on Shared Memory.
These include **Warp Synchronization** (ensuring memory visibility at the warp level) and **Warp Shuffles** (exchanging register values directly).

All warp shuffle operations require an active thread mask (`uint mask`) to coordinate synchronization under modern GPU Independent Thread Scheduling.

_(For an in-depth explanation of active masks, deadlock prevention, and shuffle patterns, refer to the [Warp Primitives and Thread Masking Guide](../04_guides/01_warp_programming_guide.md). For method signatures, see the [Gpu Class Reference](../03_bcl_api/gpu_class/00_index.md))._


## 4.3 Memory Consistency and Synchronization

Because threads execute concurrently and asynchronously, G# provides explicit mechanisms for synchronization and memory visibility.

### 4.3.1 Barrier Synchronization

> [!WARNING]
> If a block barrier is placed inside a divergent branch (`if`), it will cause a hardware deadlock and crash the GPU driver, as disabled threads will never reach the barrier.

Calling a thread block barrier (`Gpu.SyncThreads()`) creates a synchronization point for all threads within a **Thread Block**.
_(For detailed usage, refer to the BCL reference [Gpu.SyncThreads](../03_bcl_api/gpu_class/SyncThreads.md))._

- **Rule:** No thread in the block can proceed past the barrier until all threads in the block have reached it. The CPU paths keep this rule also when the threads reach the barrier after different numbers of steps, such as a warp shuffle that only some warps run.
- **Use Case:** Primarily used to ensure that all threads have finished writing to shared memory before any thread attempts to read from it, preventing Read-After-Write (RAW) hazards.

### 4.3.2 Atomic Operations

> [!NOTE]
> **Solution:** G# provides Atomic intrinsic operations. These operations perform a Read-Modify-Write sequence indivisibly, guaranteeing correct results at the cost of serialization contention. _(Refer to the BCL reference [AtomicAdd](../03_bcl_api/gpu_class/AtomicAdd.md) and [Gpu Class Reference](../03_bcl_api/gpu_class/00_index.md) for available atomic methods)._

When multiple threads across the Grid need to update the same global memory address concurrently, standard arithmetic operators (e.g., `sum += val`) will cause race conditions resulting in data loss.

### 4.3.3 Cooperative Groups (Grid-Level Synchronization)

Standard CUDA lacks a built-in mechanism to synchronize all threads across an entire Grid (across different blocks). However, G# supports **Cooperative Kernels**.

- This acts as a global barrier, ensuring all blocks have reached the point before continuing. This is heavily utilized in multi-stage reduction algorithms (e.g., Large Number Multiplication or Number Theoretic Transform).
  _(Refer to the BCL reference [Gpu Class Reference](../03_bcl_api/gpu_class/00_index.md) for grid synchronization and [GpuKernelAttribute](../03_bcl_api/attributes/GpuKernelAttribute.md) for `[GpuKernel(Cooperative = true)]`)._
- The CPU paths (native and managed) run a cooperative kernel in grid phases: every block reaches a grid sync before any thread goes past it, so the result is the GPU's, whatever the number of host threads. A block, or thread 0 of a block, that finishes while the others wait at a grid sync, and a grid sync reached in a kernel that is not cooperative, throw `InvalidOperationException` there; the GPU never completes such a grid sync, or does not define it ([SyncGrid](../03_bcl_api/gpu_class/SyncGrid.md#on-the-cpu-paths)).

## See also



### BCL API > Execution Context

- [Execution Context](../03_bcl_api/execution_context/00_index.md)

### BCL API > Gpu Class

- [Gpu Class](../03_bcl_api/gpu_class/00_index.md)
- [Gpu.SyncThreads Method (G#)](../03_bcl_api/gpu_class/SyncThreads.md)

### Guides

- [4. Practical Guides and Best Practices](../04_guides/00_index.md)
- [Warp Primitives and Thread Masking Guide](../04_guides/01_warp_programming_guide.md)

