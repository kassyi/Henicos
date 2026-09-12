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
    public string EncryptionKeyProvider { get; set; }
}
```

**Remarks**
* **Return Type**: Must be `void`. Kernels execute asynchronously on the device and cannot return a primitive value directly.
* **Accessibility**: Must be `public static` or `internal static`.
* **Calling Constraints (CRITICAL)**: A `[GpuKernel]` method **CANNOT** be called by another `[GpuKernel]` method (as CUDA does not allow `__global__` to call `__global__` without Dynamic Parallelism). Use `[GpuDeviceFunction]` instead.
* **EncryptionKeyProvider**: Allows specifying a custom static method for AOT kernel decryption (overriding VBO strategy).

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
* Translates to `inline __device__` in CUDA. Control inlining behavior using `[MethodImpl(MethodImplOptions.NoInlining)]`.

**Example**
```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
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
* **Behavior**: The parameter is removed from the generated C++ function signature. The AOT transpiler injects a `#define` macro at the top of the C++ source.
* **Loop Unrolling**: The transpiler automatically prepends `#pragma unroll` to `for` loops. If the loop bound is a `[Constant]`, NVCC will fully unroll it at compile time.

**Example**
```csharp
[GpuKernel]
public static unsafe void MultiplyKernel(int* data, [Constant] int iterations)
{
    int sum = 0;
    // NVCC will fully unroll this loop because 'iterations' is a compile-time constant macro
    for (int i = 0; i < iterations; i++) 
    {
        sum += i;
    }
    data[0] = sum;
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
`public static void AtomicAdd(ref int address, int val)`
*(Also available for `uint`, `float`, `ulong`)*

**Remarks**
Maps to `atomicAdd(&address, val)`. Prevents race conditions when multiple threads concurrently write to the same global or shared memory location.

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
* **Static Allocation**: If instantiated with a compile-time constant size (or `[Constant]`), it transpiles to a static `__shared__ T smem[Size]`.
* **Dynamic Allocation**: If instantiated with a dynamic variable, it utilizes the `extern __shared__ char __gsharp_dynamic_smem[]` buffer with dynamic pointer alignment. Host launcher must supply `sharedMemBytes`.
* **No Bounds Checking**: Attempting to index out of bounds will result in silent memory corruption or a GPU fault.

**Example**
```csharp
[GpuKernel]
public static unsafe void DynamicSmemKernel([Constant] int size1, int dynamicSize)
{
    // Static Allocation (Size known at compile time)
    SharedMemory<int> staticSmem = new(256);
    SharedMemory<int> staticSmem2 = new(size1); 

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
