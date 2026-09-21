# 4. Shared Memory Reference (`SharedMemory<T>`)

The `SharedMemory<T>` struct represents a block of memory allocated in the Streaming Multiprocessor's (SM) low-latency shared memory space (mapped to CUDA's `__shared__` memory).

Because shared memory resides physically on the SM rather than in external VRAM, it is incredibly fast (comparable to L1 cache). It is primarily used for cooperative thread communication, reductions, and buffering data to avoid redundant global memory reads.

---

## 4.1 Struct Definition

#### Syntax
```csharp
public readonly ref struct SharedMemory<T> where T : unmanaged
{
    public SharedMemory(int size);
    public T this[int index] { get; set; }
}
```

> [!WARNING]
> `SharedMemory<T>` is a `ref struct`. It can only exist on the stack and cannot be boxed, stored in an array, or used inside async methods. It is strictly limited to execution within `[GpuKernel]` functions.

---

## 4.2 Instantiation and Lifecycle

Unlike standard C# allocations, instantiating `SharedMemory<T>` inside a kernel does **not** allocate memory dynamically on the heap. Instead, the G# transpiler intercepts this call and emits a static or dynamic `__shared__` array declaration at the top of the generated C++ function.

The `size` parameter determines how many elements of type `T` are allocated.

- **Static Allocation**: If instantiated with a compile-time constant size (e.g., `new SharedMemory<int>(256)`), it transpiles to a static `__shared__ T smem[Size]`.
- **Dynamic Allocation**: If instantiated with a dynamic variable (e.g., `new SharedMemory<int>(dynamicSize)`), it utilizes the `extern __shared__ char __gsharp_dynamic_smem[]` buffer with dynamic pointer alignment. The total required bytes must be supplied via the `sharedMemBytes` parameter in the host launcher.

```csharp
[GpuKernel]
public static void SharedMemoryExample(int dynamicSize)
{
    // Static Allocation (Size known at compile time)
    var staticData = new SharedMemory<int>(256);

    // Dynamic Allocation (Size determined at runtime)
    var dynamicData = new SharedMemory<float>(dynamicSize);
}
```

> [!IMPORTANT]
> A `SharedMemory<T>` block is shared among **all threads in the same Thread Block**. If Thread 0 writes a value to `sharedData[0]`, Thread 1 can read that exact value, provided proper synchronization is applied.

---

## 4.3 Mandatory Synchronization

Because all threads in a block execute concurrently, reading and writing to shared memory requires explicit barrier synchronization to prevent race conditions.
You must call `Gpu.SyncThreads()` after writing to shared memory and before reading from it.

#### Example: Cooperative Load and Process
```csharp
[GpuKernel]
public static void MovingAverage(float[] input, float[] output)
{
    // Allocate shared memory for the block
    var shared = new SharedMemory<float>(256);
    int tid = ThreadIndex.X;
    int globalId = BlockIndex.X * 256 + tid;

    // Phase 1: All threads cooperatively load data from slow global memory to fast shared memory
    shared[tid] = input[globalId];

    // BARRIER: Wait for all threads to finish writing
    Gpu.SyncThreads();

    // Phase 2: Compute a moving average using neighboring values in shared memory
    float sum = shared[tid];
    if (tid > 0) sum += shared[tid - 1];
    if (tid < 255) sum += shared[tid + 1];

    output[globalId] = sum / 3.0f;
}
```

---

## 4.4 Bank Conflicts

> [!NOTE]
> **Conceptual Model: The Mechanism of Bank Conflicts**
> 
> To achieve massive throughput, Shared Memory is physically divided into 32 independent memory modules called **Banks** (analogous to 32 independent tellers at a bank). 
> 
> Because they are independent, all 32 tellers can serve 32 threads simultaneously, provided every thread asks a *different* teller for data. 
> Consecutive memory addresses (`shared[0]`, `shared[1]`, `shared[2]`) are hardware-mapped to consecutive Banks (Bank 0, Bank 1, Bank 2).
> 
> A **Bank Conflict** occurs when multiple threads in a warp try to access different memory addresses that happen to be assigned to the *same* Bank (the same teller). The hardware cannot serve them simultaneously. Instead, it forces the threads to form a line and serves them sequentially, effectively dividing your memory bandwidth by the number of conflicting threads.

Shared memory in NVIDIA GPUs is divided into memory banks. If multiple threads access different memory addresses that map to the same bank simultaneously, the hardware must serialize the memory requests, drastically reducing throughput.

To maximize performance, ensure that threads with adjacent IDs (e.g., Thread 0, 1, 2) access adjacent indices in the `SharedMemory<T>` array. This guarantees that each thread interacts with a distinct memory bank. Avoid strided access patterns (e.g., Thread 0 accessing `shared[0]`, Thread 1 accessing `shared[32]`) unless all threads are explicitly broadcasting from the exact same address.
