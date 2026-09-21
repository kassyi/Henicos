# 2. `Kassyi.GSharp.Core.Gpu` Class Reference

The `Gpu` class provides static methods that expose NVIDIA CUDA hardware intrinsics, synchronization primitives, memory barriers, atomic operations, and transpiler macro markers.

All methods within this class are decorated with `[CudaIntrinsic]` or handled directly by the G# Roslyn transpiler. They are strictly restricted to execution within a `[GpuKernel]` or `[GpuDeviceFunction]`.

> [!WARNING]
> Attempting to call any method in the `Gpu` class from the host CPU (standard .NET runtime) will instantly throw a `GpuOnlyException`.

---

## 2.1 Synchronization and Barriers

### `Gpu.SyncThreads()`

Synchronizes all threads within the current Thread Block (maps to `__syncthreads()`).

#### Syntax

```csharp
public static void SyncThreads()
```

#### Remarks

This method establishes a barrier synchronization point. All threads in a block must reach this point before any thread is allowed to proceed past it. It is heavily used when threads are cooperatively reading and writing to `SharedMemory<T>`.

> [!CAUTION]
> **Warp Divergence Hazard:** If `Gpu.SyncThreads()` is placed inside a divergent branch (e.g., an `if` block that is not evaluated to `true` by _all_ threads in the block), the GPU will hang or cause undefined behavior because the waiting threads will never be signaled by the divergent threads.

#### Example

```csharp
[GpuKernel]
public static void SharedMemoryReduction(int[] input, int[] output)
{
    var shared = new SharedMemory<int>(256);
    int tid = ThreadIndex.X;

    // Load data from global to shared memory
    shared[tid] = input[BlockIndex.X * 256 + tid];

    // Ensure all threads have finished writing before reduction begins
    Gpu.SyncThreads();

    // ... reduction logic ...
}
```

### `Gpu.SyncGrid()`

Synchronizes all threads across the entire cooperative grid (maps to `cg::this_grid().sync()`).

#### Syntax

```csharp
public static void SyncGrid()
```

#### Remarks

Requires the kernel to be launched via Cooperative Groups API on the host (`cuLaunchCooperativeKernel`). If invoked in a standard kernel launch, it may result in an illegal memory access or trap.

---

## 2.2 Compile-Time Loop Unrolling

### `Gpu.ForUnroll(int start, int count, Action<int> body)`

A transpilation macro that forces the NVIDIA compiler to unroll the loop body completely at compile-time.

#### Syntax

```csharp
public static void ForUnroll(int start, int count, Action<int> body)
public static void ForUnroll<TContext>(ref TContext ctx, int start, int count, UnrollAction<TContext> body)
```

#### Parameters

- `start` (`int`): The starting index of the loop. **Must be a compile-time constant.**
- `count` (`int`): The number of iterations to unroll. **Must be a compile-time constant.**
- `body` (`Action<int>` or `UnrollAction<TContext>`): The lambda expression, local function, or method group to execute per iteration.

#### Remarks

Unlike a standard `for` loop which generates branching PTX instructions, `Gpu.ForUnroll` instructs the Roslyn AST generator to physically duplicate the AST nodes of the `body` delegate `count` times.
This is critical for high-performance register indexing, avoiding loop overhead, and enabling the NVCC backend to heavily pipeline instructions.

> [!NOTE]
> When passing a lambda expression, G# optimizes away the delegate allocation (Closure/Class creation) via its Zero-Allocation guarantee, replacing it directly with inline block statements.

#### Example

```csharp
[GpuKernel]
public static void UnrollExample(float[] data)
{
    float sum = 0;

    // Generates 4 sequential inline additions in PTX without loop branching.
    Gpu.ForUnroll(0, 4, i =>
    {
        sum += data[i];
    });
}
```

---

## 2.3 Warp-Level Primitives (Shuffle)

> [!NOTE]
> **Conceptual Model: Warp Execution and Shuffles**
>
> In the CUDA architecture, threads are grouped into sets of 32 called a **Warp**. A warp executes in *Lockstep*—meaning all 32 threads are physically bound to execute the exact same instruction at the exact same clock cycle. 
>
> Traditionally, if Thread 0 needs to share a computed value with Thread 1, Thread 0 must write that value to **Shared Memory** and wait for Thread 1 to read it. This requires memory allocation and incurs latency.
>
> A **Shuffle** is a hardware-level register exchange. Because all 32 threads are executing the same instruction simultaneously, the hardware allows them to directly read values from each other's private **Registers**. It is the absolute fastest method for data exchange on a GPU, bypassing RAM entirely.

Warp shuffle functions allow threads within the same warp to exchange data directly via registers.

### `Gpu.ActiveMask()`

Before initiating a shuffle, you must provide a bitmask indicating which of the 32 threads are currently active and participating. This method returns that 32-bit mask.

#### Syntax

```csharp
public static uint ActiveMask()
```

### `Gpu.ShuffleSync()`

Retrieves a variable from a specific thread (lane) within the warp.

#### Syntax

```csharp
public static int ShuffleSync(uint mask, int var, int srcLane, int width = 32)
public static float ShuffleSync(uint mask, float var, int srcLane, int width = 32)
```

**Behavior:** All active threads in the warp receive the value of `var` that is currently held by the thread at `srcLane`. For example, if `srcLane` is 5, every thread receives Thread 5's value.

### `Gpu.ShuffleDownSync()`

Retrieves a variable from a thread with a higher ID relative to the caller.

#### Syntax

```csharp
public static int ShuffleDownSync(uint mask, int var, uint delta, int width = 32)
```

**Behavior:** Each thread retrieves the value from the thread located `delta` lanes to its right. Thread 0 retrieves from Thread 2, Thread 1 retrieves from Thread 3, etc. Threads near the end of the warp that attempt to read out of bounds will receive their own unmodified value.

---

#### Example (Warp-Level Reduction)

If we want to calculate the sum of 32 numbers, normally one thread would loop 32 times ($O(N)$).
Using `ShuffleDownSync`, all 32 threads can cooperate to "fold" the sum in half repeatedly, finishing in just 5 steps ($O(\log_2 N)$).

Here is how the fold happens:

1. `delta = 16`: The first 16 threads grab numbers from the last 16 threads and add them together.
2. `delta = 8`: The first 8 threads grab numbers from the next 8 threads.
3. This continues until Thread 0 holds the final sum.

```csharp
[GpuKernel]
public static void WarpSum(int[] data, int[] result)
{
    // A warp has 32 threads. Lane ID is from 0 to 31.
    int laneId = ThreadIndex.X % 32;

    // Each thread reads one number from memory into its private register
    int val = data[ThreadIndex.X];

    // We assume all 32 threads in the warp are active and participating
    uint mask = 0xFFFFFFFF;

    // Step 1: Thread 0..15 add values from Thread 16..31
    val += Gpu.ShuffleDownSync(mask, val, 16);

    // Step 2: Thread 0..7 add values from Thread 8..15
    val += Gpu.ShuffleDownSync(mask, val, 8);

    // Step 3: Thread 0..3 add values from Thread 4..7
    val += Gpu.ShuffleDownSync(mask, val, 4);

    // Step 4: Thread 0..1 add values from Thread 2..3
    val += Gpu.ShuffleDownSync(mask, val, 2);

    // Step 5: Thread 0 adds value from Thread 1
    val += Gpu.ShuffleDownSync(mask, val, 1);

    // At this exact moment, Thread 0's 'val' contains the sum of all 32 numbers!
    // The other 31 threads hold garbage/intermediate values, which is fine.

    // Only Thread 0 writes the final answer back to slow global memory
    if (laneId == 0)
    {
        result[ThreadIndex.X / 32] = val;
    }
}
```

---

## 2.4 Atomics

Atomic operations read, modify, and write a value back to memory in one indivisible transaction. They guarantee that no other thread can access the address until the operation is fully complete.

### `Gpu.AtomicAdd` / `Gpu.AtomicSub` / `Gpu.AtomicOr` / `Gpu.AtomicMax`

#### Syntax

```csharp
public static int AtomicAdd(ref int address, int val)
public static float AtomicAdd(ref float address, float val)
public static ulong AtomicMax(ref ulong address, ulong val)
```

#### Returns

The **old** value that resided at `address` immediately prior to the operation.

#### Example

```csharp
[GpuKernel]
public static void Histogram(int[] pixels, int[] bins)
{
    int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
    int color = pixels[tid];

    // Safely increment the bin counter across thousands of threads
    Gpu.AtomicAdd(ref bins[color], 1);
}
```

---

## 2.5 Execution Scoping

### `Gpu.SingleThread(Action body)`

Ensures that the enclosed action is executed exclusively by thread 0 of block 0 (`threadIdx.x == 0 && blockIdx.x == 0`).

#### Syntax

```csharp
public static void SingleThread(Action body)
```

#### Remarks

This is extremely useful for performing global initialization or printing diagnostic logs that should only occur once per grid launch, rather than thousands of times.

#### Example

```csharp
[GpuKernel]
public static void ProcessData()
{
    Gpu.SingleThread(() =>
    {
        Logger.LogInfo("Kernel execution started.");
    });

    // All threads proceed to execute the rest of the kernel
}
```

---

## 2.6 Transpiler Macros

Transpiler macros are structural language constructs intercepted at compile-time by the G# Roslyn Source Generator. They expand into optimized native CUDA loop patterns without runtime delegate invocation overhead.

### `Gpu.GridStrideLoop`

Generates a standard CUDA Grid-Stride Loop over the specified upper bound across all threads and blocks.

#### Syntax

```csharp
public static void GridStrideLoop(int maxBounds, Action<int> body)
public static void GridStrideLoop<TContext>(ref TContext ctx, int maxBounds, GridStrideAction<TContext> body)
```

#### Transpiled CUDA C++ Equivalent

```cpp
for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < maxBounds; i += blockDim.x * gridDim.x)
{
    // Inlined loop body
}
```

#### Remarks

Grid-stride loops are the recommended CUDA best practice for general array computation because they:
1. **Decouple Buffer Size from Grid Launch Dimensions**: The kernel executes correctly regardless of whether the array length is smaller than, equal to, or significantly larger than the total number of hardware threads launched.
2. **Promote Memory Coalescing**: Adjacent threads iterate with stride `blockDim.x * gridDim.x`, maximizing memory bandwidth utilization.
3. **Eliminate Boilerplate**: Automatically avoids out-of-bounds index calculations.

#### Example

```csharp
[GpuKernel]
public static void VectorAdd(float[] a, float[] b, float[] c, int n)
{
    Gpu.GridStrideLoop(n, i =>
    {
        c[i] = a[i] + b[i];
    });
}
```

---

### `Gpu.ForUnroll`

Unrolls a fixed-count loop entirely at compile-time, eliminating branch instructions and induction variable overhead.

#### Syntax

```csharp
public static void ForUnroll(int start, int count, Action<int> body)
public static void ForUnroll<TContext>(ref TContext ctx, int start, int count, UnrollAction<TContext> body)
```

#### Remarks

The `start` and `count` parameters must be compile-time constants or literals. The G# Source Generator inlines each iteration consecutively into the generated CUDA C++ AST.

#### Example

```csharp
[GpuKernel]
public static void Compute4x4Stencil(float[] output, float[] input)
{
    int baseIdx = ThreadIndex.X * 4;

    // Unrolled directly into 4 contiguous instructions with no runtime loop branch
    Gpu.ForUnroll(0, 4, offset =>
    {
        output[baseIdx + offset] = input[baseIdx + offset] * 1.5f;
    });
}
```

### `Gpu.Constant`

Directly injects a given string literal or identifier token as a raw C++ expression (e.g., `#define` text or enum values) into the transpiled source.

#### Syntax

```csharp
public static void Constant(string expression)
```

#### Remarks

This macro is useful when you need to pass raw C++ identifiers or preprocessor macros directly into the emitted CUDA C++ code, which would otherwise be impossible to express in standard C#.

#### Example

```csharp
[GpuKernel]
public static void UseNativeConstant(int* output)
{
    // Emits `output[0] = SOME_CPP_MACRO;` natively
    output[0] = Gpu.Constant("SOME_CPP_MACRO");
}
```

---

