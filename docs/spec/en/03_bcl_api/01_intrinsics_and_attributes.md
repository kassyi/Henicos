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
* **Return Type**: Can be `void`, unmanaged value types, or single-dimensional arrays. If a non-void type is returned, the generated launcher automatically allocates and returns a host array containing the returned values from each thread.
* **Accessibility**: Must be `public static` or `internal static`.
* **Calling Constraints (CRITICAL)**: A `[GpuKernel]` method **CANNOT** be called by another `[GpuKernel]` method (as CUDA does not allow `__global__` to call `__global__` without Dynamic Parallelism). Use `[GpuDeviceFunction]` instead.
* **EncryptionKeyProvider**: Allows specifying a custom static method for AOT kernel decryption (overriding VBO strategy).
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
* **`[CudaIntrinsicPattern]`**: Maps a C# method to a templated or patterned C++ expression (e.g., `__gsharp_add_cc({0}, {1}, {2})`).
* **`[BclIntrinsicMap]`**: Marks a struct as a direct map to a BCL type, directing the transpiler to use the mapped implementations.
* **`[GpuOperatorMacro]`**: Maps a C# method to a lambda expression or macro string for inline transpilation.
* **`[SkipSnapshotConsistency]`**: Used in testing to bypass snapshot consistency verification for specific kernels.

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

### `Gpu` Macro Methods

The `Gpu` class provides high-level macro methods that are expanded directly into optimized C++ control flow or text by the transpiler.

* **`Gpu.GridStrideLoop(int count, Action<int> body)`**: Expands into a standard CUDA grid-stride loop (`for (int i = blockIdx.x * blockDim.x + threadIdx.x; i < count; i += blockDim.x * gridDim.x)`).
* **`Gpu.SingleThread(Action body)`**: Wraps the body in a thread execution guard (`if (threadIdx.x == 0 && blockIdx.x == 0) { ... }`), ensuring the code runs only once per grid.
* **`Gpu.Constant(string expression)`**: Directly injects the given string literal or identifier token as a raw C++ constant expression (e.g., `#define` text or enum values) into the transpiled source.

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
