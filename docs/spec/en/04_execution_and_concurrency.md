# 4. Execution and Concurrency Model

G# is designed strictly for Massively Parallel Processor (MPP) architectures, mapping C# constructs directly to NVIDIA's CUDA execution model. Understanding this model is critical for writing correct and performant G# code.

## 4.1 The Thread Hierarchy

G# kernels are executed by an array of threads. These threads are organized into a two-level hierarchy:

1. **Thread Block:** A group of threads that execute concurrently on a single Streaming Multiprocessor (SM). Threads within the same block can cooperate via **Shared Memory** and synchronize their execution using **Barrier Synchronization** (`Gpu.SyncThreads()`).
2. **Grid:** An array of Thread Blocks that execute the same kernel. Blocks within a Grid are executed independently and can be scheduled in any order across the available SMs.

### 4.1.1 Indexing

To determine their unique identity and workload, threads access built-in index variables through the `ThreadIndex`, `BlockIndex`, `BlockDimension`, and `GridDimension` structs provided by `Kassyi.GSharp.Core`.

```csharp
// Context: G# Kernel (GPU)
// Example: Calculating a global 1D thread ID
int globalThreadId = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
```

## 4.2 Warp Execution Model

At the hardware level, Thread Blocks are partitioned into **Warps** (groups of 32 threads in current NVIDIA architectures). A warp executes one common instruction at a time.

### 4.2.1 Warp Divergence

When threads within the same warp take different execution paths due to data-dependent conditional branching (`if`/`else`), the warp **diverges**.

* **Behavior:** The hardware executes each branch path serially, disabling threads that are not on that path. When all paths complete, the threads converge back to the same execution path.

> Warp divergence severely degrades performance. G# developers should avoid branching on condition variables that vary across threads within a warp (e.g., `ThreadIndex.X`). Condition variables based on `BlockIndex.X` or `[Constant]` parameters do not cause divergence within a block.

### 4.2.2 Warp Intrinsics (Synchronization and Shuffle)

G# natively supports Warp-level intrinsics, allowing threads within the same warp to communicate and synchronize efficiently without relying on Shared Memory.

* **Synchronization:** `Gpu.SyncWarp()` synchronizes threads within the same warp (mapping to `__syncwarp()`), ensuring memory visibility and instruction ordering at the warp level.
* **Warp Shuffle:** Operations like `Gpu.Shuffle`, `Gpu.ShuffleUp`, and `Gpu.ShuffleDown` permit threads in a warp to directly exchange register values. This is critical for high-performance parallel reductions and scans.

## 4.3 Memory Consistency and Synchronization

Because threads execute concurrently and asynchronously, G# provides explicit mechanisms for synchronization and memory visibility.

### 4.3.1 Barrier Synchronization (`__syncthreads()`)

Calling `Gpu.SyncThreads()` creates a barrier for all threads within a **Thread Block**.

* **Rule:** No thread in the block can proceed past the `Gpu.SyncThreads()` barrier until all threads in the block have reached it.
* **Use Case:** Primarily used to ensure that all threads have finished writing to a `SharedMemory<T>` array before any thread attempts to read from it, preventing Read-After-Write (RAW) hazards.

> [!WARNING]
> If `Gpu.SyncThreads()` is placed inside a divergent branch (`if`), it will cause a hardware deadlock and crash the GPU driver, as disabled threads will never reach the barrier.

### 4.3.2 Atomic Operations

When multiple threads across the Grid need to update the same global memory address concurrently, standard arithmetic operators (e.g., `sum += val`) will cause race conditions resulting in data loss.

> [!NOTE]
> **Solution:** G# provides `Gpu.AtomicAdd(ref T address, T val)` and other atomic intrinsics. These operations perform a Read-Modify-Write sequence indivisibly, guaranteeing correct results at the cost of serialization contention.

### 4.3.3 Cooperative Groups (Grid-Level Synchronization)

Standard CUDA lacks a built-in mechanism to synchronize all threads across an entire Grid (across different blocks). However, G# supports **Cooperative Kernels**.

* Decorating a kernel with `[GpuKernel(Cooperative = true)]` allows the use of `Gpu.SyncGrid()`.
* This acts as a global barrier, ensuring all blocks have reached the point before continuing. This is heavily utilized in multi-stage reduction algorithms (e.g., Large Number Multiplication or Number Theoretic Transform).
