# 1. Attributes and Intrinsics (Kassyi.GSharp.Core)

This document provides the API Reference for the core attributes, intrinsic APIs, and built-ins provided by `Kassyi.GSharp.Core`. These constructs provide C# IntelliSense support during development but are dynamically intercepted and replaced by the AOT transpiler with raw CUDA equivalents.

---

## Part A: Core Attributes

G# introduces specialized C# attributes that govern compilation, entry point semantics, and JIT optimizations.

### `[GpuKernel]` Attribute

The `[GpuKernel]` attribute acts as the entry point marker for a G# transpilation process. It translates directly to the `__global__` specifier in CUDA C++.

**Syntax**
```csharp
[AttributeUsage(AttributeTargets.Method)]
public class GpuKernelAttribute : Attribute
{
    public bool Cooperative { get; set; }
}
```

**Remarks**
* **Return Type**: Can be `void`, unmanaged value types, or single-dimensional arrays. If a non-void type is returned, the generated launcher automatically allocates and returns a host array containing the returned values from each thread.
* **Accessibility**: Must be `public static` or `internal static`.
* **Calling Constraints (CRITICAL)**: A `[GpuKernel]` method **CANNOT** be called by another `[GpuKernel]` method (as CUDA does not allow `__global__` to call `__global__` without Dynamic Parallelism). Use `[GpuDeviceFunction]` instead.
* **Cooperative**: If set to `true`, the kernel is compiled and launched as a Cooperative Kernel (`cuLaunchCooperativeKernel`), allowing grid-wide synchronization via `Gpu.SyncGrid()`.

**Example**
```csharp
// Context: Host C# and G# Kernel
public partial class VectorMath
{
    [GpuKernel]
    public static unsafe void AddVectors(float* a, float* b, float* result, int length)
    {
        int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        if (tid < length)
        {
            result[tid] = a[tid] + b[tid];
        }
    }
}
```

### `[GpuDeviceFunction]` Attribute

Marks a helper method intended for execution solely on the GPU device.

**Syntax**
```csharp
[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceFunctionAttribute : Attribute
```

**Remarks**
* Can return primitive types.
* Can only be called by `[GpuKernel]` methods or other `[GpuDeviceFunction]` methods.
* Translates to `__device__` in CUDA.
* Control inlining behavior using the `[GpuInline]` (translates to `__forceinline__`) or `[GpuNoInline]` (translates to `__noinline__`) custom attributes. (Note: The standard `[MethodImpl]` attribute is ignored by the transpiler).

**Example**
```csharp
[GpuDeviceFunction]
[GpuInline]
public static float FastInverseSqrt(float number)
{
    // Implementation ...
    return result; // returning primitive value is allowed here
}
```

### `[Constant]` Attribute

Marks a kernel argument as an Ahead-Of-Time (AOT) preprocessor constant, enabling aggressive hardware-level optimizations (e.g., loop unrolling).

**Syntax**
```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public class ConstantAttribute : Attribute
```

**Remarks**
* **Allowed Types**: Primitive integral types (`int`, `uint`, etc.).
* **Behavior**: The parameter is removed from the generated C++ function signature. The AOT transpiler injects a `#define` macro at the top of the C++ source, allowing the backend C++ compiler (like NVCC) to perform aggressive compile-time optimizations such as dead-code elimination.

**Example**
```csharp
[GpuKernel]
public static unsafe void MultiplyKernel(int* data, [Constant] int iterations)
{
    int sum = 0;
    // The backend compiler can heavily optimize this loop because 'iterations' is a macro
    for (int i = 0; i < iterations; i++) 
    {
        sum += i;
    }
    data[0] = sum;
}
```

### Parameter Interop Attributes (`[In]`, `[Out]`)

Standard .NET attributes `[In]` and `[Out]` from `System.Runtime.InteropServices` can be applied to array parameters to optimize memory transfers across the PCIe bus.

**Syntax**
```csharp
using System.Runtime.InteropServices;

[GpuKernel]
public static void VectorAdd([In] float[] a, [In] float[] b, [Out] float[] c, int n)
```

**Remarks**
* By default, G# assumes all arrays are read/write (`[In, Out]`) and generates AOT wrapper code that copies data both to the device before kernel execution and back to the host after execution.
* **`[In]` Attribute**: Indicates the array is read-only. The AOT transpiler will **skip copying the array back** to the host (`cuMemcpyDtoH`) after execution, saving PCIe bandwidth.
* **`[Out]` Attribute**: Indicates the array is write-only. The AOT transpiler will **skip copying the array to** the device (`cuMemcpyHtoD`) before execution. It still allocates memory on the device (`cuMemAlloc`).
* Using these attributes appropriately is highly recommended for performance-critical kernels, as redundant PCIe transfers can drastically degrade performance compared to native CUDA.

**Example**
```csharp
[GpuKernel]
public static unsafe void HashKernel([In] byte[] input, [Out] uint[] output, int numMessages)
{
    // The AOT wrapper will only copy 'input' to the GPU, and only copy 'output' back to the CPU.
}
```

### Advanced Mapping Attributes

The `Kassyi.GSharp.Core` package includes advanced attributes used primarily by the standard library to map C# constructs to native CUDA C++ representations during transpilation.
* **`[CudaIntrinsic]`**: Maps a C# method to a specific CUDA C++ function call.
* **`[CudaIntrinsicPattern]`**: A Class/Struct-level attribute used to map all members of a type to a C++ pattern. The `{0}` token represents the lowercase name of the invoked member (e.g., `[CudaIntrinsicPattern("threadIdx.{0}")]` maps `ThreadIndex.X` to `threadIdx.x`).
* **`[BclIntrinsicMap]`**: An assembly-level attribute used by the transpiler to map specific BCL method signatures (e.g., `System.Numerics.BitOperations.PopCount`) directly to GPU hardware intrinsics (e.g., `__popc`).
* **`[GpuOperatorMacro]`**: Maps C# operator overloads (like `+` or `*`) to specific GPU device functions via `TargetType` and `TargetMethod`.
* **`[SkipSnapshotConsistency]`**: Used in testing to bypass snapshot consistency verification for specific kernels.

### `[TakesOwnership]` Attribute

Marks a dependency-injected GPU resource as being owned by the receiving class.

**Syntax**
```csharp
[AttributeUsage(AttributeTargets.Parameter)]
public class TakesOwnershipAttribute : Attribute
```

**Remarks**
* **Memory Safety / Zero-Leak Analyzer**: By default, injecting a disposable GPU resource (like `GpuContext` or `GpuBuffer<T>`) into a constructor implies **Borrowing**. The analyzer (e.g., `GSH021`, `GSH022`) enforces that borrowed resources must *not* be disposed by the receiving class to prevent double-free errors.
* Applying `[TakesOwnership]` explicitly transfers ownership to the receiving class. The analyzer then enforces that the receiving class must implement `IDisposable` and must properly dispose of the resource.

**Example**
```csharp
public class ComputePipeline : IDisposable
{
    private readonly GpuContext _context;

    // Explicitly takes ownership of the injected context
    public ComputePipeline([TakesOwnership] GpuContext context)
    {
        _context = context;
    }

    public void Dispose()
    {
        // Enforced by the Zero-Leak Analyzer
        _context.Dispose();
    }
}
```

---

## Part B: Intrinsics and Built-ins

### Thread and Block Indices

The `ThreadIndex`, `BlockIndex`, `BlockDimension`, and `GridDimension` static classes provide access to CUDA's built-in execution context variables.

**Syntax**
```csharp
public static class ThreadIndex { public static int X { get; } public static int Y { get; } public static int Z { get; } }
public static class BlockIndex { /* X, Y, Z */ }
public static class BlockDimension { /* X, Y, Z */ }
public static class GridDimension { /* X, Y, Z */ }
```

**Remarks**
Accessing these properties on the CPU (host) will throw a `GpuOnlyException`. They map directly to `threadIdx.x`, `blockIdx.x`, etc.

**Example**
```csharp
[GpuKernel]
public static unsafe void Process1D(float* data)
{
    int globalId = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
    data[globalId] *= 2.0f;
}
```

### `Gpu.SyncThreads` Method

Creates a barrier synchronization point for all threads within a block.

**Syntax**
`public static void SyncThreads()`

**Remarks**
Maps to `__syncthreads()`. Ensures that all threads in the block have reached this point before any thread proceeds. Crucial for avoiding race conditions when utilizing Shared Memory.

**Example**
```csharp
[GpuKernel]
public static unsafe void SharedMemoryDemo(int* data)
{
    SharedMemory<int> smem = new(256);
    int tid = ThreadIndex.X;
    
    // 1. Load data into shared memory
    smem[tid] = data[tid];
    
    // 2. Wait for all threads to finish loading
    Gpu.SyncThreads(); 
    
    // 3. Safely read from other threads' shared memory slots
    if (tid > 0) {
        data[tid] = smem[tid] + smem[tid - 1];
    }
}
```

### `Gpu.AtomicAdd` Method

Performs an atomic read-modify-write addition to a memory address.

**Syntax**
```csharp
public static int AtomicAdd(ref int address, int val)
public static uint AtomicAdd(ref uint address, uint val)
public static float AtomicAdd(ref float address, float val)
public static ulong AtomicAdd(ref ulong address, ulong val)
```

**Remarks**
Maps to `atomicAdd(&address, val)`. Prevents race conditions when multiple threads concurrently write to the same global or shared memory location. The returned value is the old value observed at `address` before the addition occurred.

**Example**
```csharp
[GpuKernel]
public static unsafe void HistogramKernel(int* data, int* bins)
{
    int value = data[ThreadIndex.X];
    // Atomically increment the bin counter to prevent data loss
    Gpu.AtomicAdd(ref bins[value], 1); 
}
```

### `SharedMemory<T>` Struct

Abstracts CUDA's `__shared__` memory space for ultra-fast, block-local data sharing.

**Syntax**
`public ref struct SharedMemory<T> where T : unmanaged`

**Remarks**
* **Static Allocation**: If instantiated with a compile-time constant size, it transpiles to a static `__shared__ T smem[Size]`.
* **Dynamic Allocation**: If instantiated with a dynamic variable, it utilizes the `extern __shared__ char __gsharp_dynamic_smem[]` buffer with dynamic pointer alignment. Host launcher must supply `sharedMemBytes`.
* **No Bounds Checking**: Attempting to index out of bounds will result in silent memory corruption or a GPU fault.

**Example**
```csharp
[GpuKernel]
public static unsafe void DynamicSmemKernel(int dynamicSize)
{
    // Static Allocation (Size known at compile time)
    SharedMemory<int> staticSmem = new(256);

    // Dynamic Allocation (Size determined at runtime)
    SharedMemory<float> dynamicSmem = new(dynamicSize); 
}
```

### `Gpu.InlinePtx` Method

Injects raw PTX or CUDA C++ strings directly into the generated source code.

**Syntax**
`public static void InlinePtx(string ptxCode)`

**Remarks**
Supports C# string interpolation (`$`). Interpolated variables are safely expanded into the C++ AST stream at compile time, eliminating the need to construct `sprintf` on the GPU.

**Example**
```csharp
[GpuKernel]
public static void PtxDemo()
{
    uint result = 0;
    int offset = 10;
    // Injecting raw PTX with interpolated C# variable
    Gpu.InlinePtx($"asm volatile (\"add.u32 %0, %1, {offset};\" : \"=r\"(result) : \"r\"(ThreadIndex.X));");
}
```

### `Gpu` Macro Methods

The `Gpu` class provides high-level macro methods that are expanded directly into optimized C++ control flow or text by the transpiler.

* **`Gpu.GridStrideLoop(int count, Action<int> body)`**: Expands into a standard CUDA grid-stride loop (`for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < count; i += blockDim.x * gridDim.x)`).
* **`Gpu.SingleThread(Action body)`**: Wraps the body in a thread execution guard (`if (threadIdx.x == 0 && blockIdx.x == 0) { ... }`), ensuring the code runs only once per grid.
* **`Gpu.Constant(string expression)`**: Directly injects the given string literal or identifier token as a raw C++ constant expression (e.g., `#define` text or enum values) into the transpiled source.

### Loop Unroll Hint (`Gpu.Hint.Unroll`)

#### Overview & Motivation

`Gpu.Hint.Unroll` is a zero-allocation compiler marker API designed to control loop unrolling behavior within GPU kernels.
Placing this method immediately before a `for` or `while` loop instructs the transpiler to emit a `#pragma unroll` directive in the generated CUDA C++ code.

**Why was it introduced over `Gpu.ForUnroll`?**
While the macro `Gpu.ForUnroll(start, count, Action<int> body)` is perfectly transpiled into a highly optimized, flat `for` loop on the GPU (with zero allocations), it presents a problem during **CPU fallback execution**. When executed natively in C#, passing a capturing lambda expression or local function to `ForUnroll` causes an implicit closure allocation on the managed heap. This violates the strict zero-allocation requirement of high-performance G# kernels.
`Gpu.Hint.Unroll` resolves this by acting as a standalone syntactic marker preceding a standard C# `for` or `while` loop. This approach guarantees zero allocations across both GPU and CPU execution contexts, bypassing delegate overhead entirely.

#### Syntax & Example

```csharp
public static void Unroll(int factor = 0)
```

- **`factor`** (Optional): The unroll factor limit.
  - A value of `0` (or omitted) instructs the compiler to fully unroll the loop if the loop bounds are known at compile time (translates to `#pragma unroll`).
  - A value of `n > 0` translates to `#pragma unroll n`, unrolling the loop exactly `n` times per evaluation.

```csharp
[GpuKernel]
public static void ProcessArray(int* data)
{
    // [1] Full Unrolling (factor = 0 / omitted)
    // Instructs the compiler to fully unroll the subsequent loop (4 iterations).
    Gpu.Hint.Unroll();
    for (int i = 0; i < 4; i++)
    {
        data[i] = i * 2;
    }

    // [2] Partial Unrolling by a Factor (e.g., factor = 2)
    // The argument `2` instructs the compiler to unroll the loop body by a factor of 2.
    // This halves the loop overhead without bloating the binary size with 100 fully unrolled instructions.
    Gpu.Hint.Unroll(2);
    for (int i = 0; i < 100; i++)
    {
        data[i] += 1;
    }
}
```

### Warp Shuffle Intrinsics

#### Overview

The `Gpu` intrinsic class provides direct access to warp-level shuffle operations (`__shfl_sync`, etc.). These instructions allow threads within the same warp (32 threads) to directly exchange or broadcast register variables without the latency overhead of accessing shared memory.

#### Available Intrinsics

- `Gpu.ShuffleSync(mask, var, srcLane)`
- `Gpu.ShuffleDownSync(mask, var, delta)`
- `Gpu.ShuffleUpSync(mask, var, delta)`
- `Gpu.ShuffleXorSync(mask, var, laneMask)`

#### Code Example: Warp Reduction

```csharp
[GpuKernel]
public static void WarpSumReduction(int* data)
{
    int val = data[ThreadIndex.X];
    uint mask = Gpu.ActiveMask();

    // Build a reduction tree by halving the offset distance
    for (uint offset = 16; offset > 0; offset /= 2)
    {
        val += Gpu.ShuffleDownSync(mask, val, offset);
    }

    // Lane 0 holds the final sum for the entire warp
    if (ThreadIndex.X % 32 == 0)
    {
        data[ThreadIndex.X] = val;
    }
}
```

### Built-in Vector Types (`System.Numerics`)

G# transparently maps `System.Numerics.Vector2`, `Vector3`, and `Vector4` to CUDA's native `float2`, `float3`, and `float4`.

**Example**
```csharp
[GpuKernel]
public static unsafe void VectorCompute(Vector3* vectors)
{
    // Seamlessly compiled to CUDA's float3 and make_float3
    Vector3 vec = vectors[ThreadIndex.X];
    vec.Y += 10.0f;
    vectors[ThreadIndex.X] = new Vector3(vec.X, vec.Y, vec.Z);
}
```

### `System.Math` and `System.MathF`

Standard C# math functions from `System.Math` and `System.MathF` are natively supported and transpiled to their CUDA hardware-accelerated intrinsic equivalents.
* **Supported Methods**: `Exp`, `Log`, `Sqrt`, `Min`, `Max`, `Abs`, etc.
* **Translation**: `Math.Sqrt(double)` translates to `sqrt()`, while `MathF.Sqrt(float)` translates to the faster single-precision `sqrtf()`.

### `System.Numerics.BitOperations`

The G# compiler supports seamless transpilation of standard .NET `System.Numerics.BitOperations` into their highly optimized PTX instruction equivalents, expanding Base Class Library (BCL) integration inside GPU Kernels.

* **Supported Methods**:
  * `PopCount` (Mapped to `__popc`)
  * `LeadingZeroCount` (Mapped to `__clz`)
  * `TrailingZeroCount`
  * `RotateLeft` / `RotateRight`

* **Analyzer Enforcement**: Invoking unsupported `BitOperations` methods triggers the analyzer diagnostic **`GSH0003`** during compilation, preventing silent failures.

### Hardware FastMath Intrinsics

To achieve maximum throughput for compute-bound mathematical kernels, G# provides the `Kassyi.GSharp.Core.FastMath` class. These intrinsics map directly to CUDA hardware-accelerated approximate instructions (e.g., `__expf`, `__logf`, `__fsqrt_rn`). They bypass standard IEEE-754 precision safeguards to maximize instruction throughput per clock cycle.

**Example**
```csharp
using Kassyi.GSharp.Core;

[GpuKernel]
public static void ComputeFastOptions(float* results, float d1)
{
    // Evaluates directly to the high-throughput __expf and __fsqrt_rn PTX instructions
    float expValue = FastMath.Exp(-0.5f * d1 * d1);
    float sqrtValue = FastMath.Sqrt(d1);
    
    results[ThreadIndex.X] = expValue * sqrtValue;
}
```
