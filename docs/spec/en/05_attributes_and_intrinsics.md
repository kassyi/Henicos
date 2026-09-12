# 5. Attributes and Intrinsics (Kassyi.GSharp.Core)

This document details the core attributes, intrinsic APIs, and built-ins provided by Kassyi.GSharp.Core. These constructs provide C# IntelliSense support during development but are dynamically intercepted and replaced by the AOT transpiler with raw CUDA equivalents.

## Part A: Core Attributes

G# introduces specialized C# attributes that govern compilation, entry point semantics, and JIT optimizations.

## 3.1 `[GpuKernel]`

The `[GpuKernel]` attribute acts as the entry point marker for a G# transpilation process. It translates directly to the `__global__` specifier in CUDA C++.

### 3.1.1 Semantic Rules
* **Target**: Methods only.
* **Return Type**: Must be `void`. Kernels execute asynchronously on the device and cannot return a primitive value directly. Use pointer arguments (e.g., `int* result`) to output data.
* **Accessibility**: Must be `public static` or `internal static`. Instance methods (requiring a `this` pointer) are strictly prohibited.
* **Calling Constraints (CRITICAL)**: Because this attribute yields a `__global__` function, a `[GpuKernel]` method **CANNOT** be called by another `[GpuKernel]` method (as CUDA does not allow `__global__` to call `__global__` without Dynamic Parallelism). Doing so will result in an `identifier undefined` compilation error from NVCC during the JIT pipeline. Subroutines must be decorated with `[GpuDeviceFunction]` instead.
* **Transpilation**: The source generator targets the decorated method and transpiles it into an `extern "C" __global__ void` CUDA kernel. 

## 3.2 `[GpuDeviceFunction]`

The `[GpuDeviceFunction]` attribute marks a helper method intended for execution solely on the GPU device, callable only by `[GpuKernel]` methods or other `[GpuDeviceFunction]` methods. It translates directly to the `__device__` specifier in CUDA C++.

### 3.2.1 Semantic Rules
* **Target**: Methods only.
* **Return Type**: Standard primitive types are permitted (unlike `[GpuKernel]`).
* **Accessibility**: Must be `public static` or `internal static`.
* **Transpilation**: The source generator transpiles the decorated method into an `inline __device__` CUDA C++ function by default.
* **Proper Usage**: Always use `[GpuDeviceFunction]` for math utilities, internal helpers, and any logic meant to be reused within larger GPU functions. Mislabeling a helper method with `[GpuKernel]` will break the compilation linkage.

### 3.2.2 Inlining Control via `[MethodImpl]`
The transpiler respects standard C# `[MethodImpl]` attributes to provide fine-grained control over the CUDA compiler's inlining behavior for `[GpuDeviceFunction]` methods. This is critical for managing GPU register pressure and instruction cache.

* `[MethodImpl(MethodImplOptions.AggressiveInlining)]`: Transpiled to `__forceinline__ __device__`. Instructs NVCC to always inline the method, eliminating function call overhead.
* `[MethodImpl(MethodImplOptions.NoInlining)]`: Transpiled to `__noinline__ __device__`. Prevents NVCC from automatically inlining large helper functions, keeping them as separate device function calls to conserve registers in the main kernel.

## 3.3 `[Constant]`

The `[Constant]` attribute marks a kernel argument as an Ahead-Of-Time (AOT) preprocessor constant. This enables aggressive hardware-level optimizations, particularly register minimization through loop unrolling.

### 3.3.1 Semantic Rules
* **Target**: Kernel parameters only.
* **Allowed Types**: Must be a primitive integral type (e.g., `int`, `uint`).
* **Behavior**:
  1. The parameter is completely removed from the generated C++ function signature.
  2. The source generator injects a preprocessor macro (`#define [ParamName] [Value]`) at the top of the C++ source string during generation.
  3. The AOT MSBuild phase calculates a unique hash for the resulting C++ string. (Note: The attribute was historically named `[JitConstant]`, but macro injection is now entirely resolved AOT).

### 3.3.2 Automatic Loop Unrolling (`#pragma unroll`)
The G# transpiler unconditionally prepends `#pragma unroll` to every `for` loop encountered in the syntax tree.
* If a loop bound utilizes a `[Constant]`, the CUDA compiler (NVCC) evaluates the macro substitution at compile time. Identifying a constant trip count, NVCC fully unrolls the loop.
* If the loop bound is dynamic, NVCC ignores the `#pragma unroll` directive safely.

**Example C# Input:**
```csharp
// Context: G# Kernel (GPU)
[GpuKernel]
public static unsafe void MultiplyKernel(int* data, [Constant] int iterations)
{
    int sum = 0;
    for (int i = 0; i < iterations; i++)
    {
        sum += i;
    }
    data[0] = sum;
}
```

**Transpiled C++ Output (with runtime injection of `iterations = 5`):**
```cpp
#define iterations 5
extern "C" __global__ void MultiplyKernel(int* data)
{
    int sum = 0;
#pragma unroll
    for(int i=0;i<iterations;i++)
    {
        sum+=i;
    }
    data[0]=sum;
}
```

## Part B: Intrinsics and Built-ins

## 7.1 Thread and Block Indices

CUDA relies heavily on built-in variables (`threadIdx`, `blockIdx`) to identify execution context. G# provides static classes to map these structures.

| G# Construct | CUDA Mapping |
| :--- | :--- |
| `ThreadIndex.X` / `Y` / `Z` | `threadIdx.x` / `y` / `z` |
| `BlockIndex.X` / `Y` / `Z` | `blockIdx.x` / `y` / `z` |
| `BlockDimension.X` / `Y` / `Z` | `blockDim.x` / `y` / `z` |
| `GridDimension.X` / `Y` / `Z` | `gridDim.x` / `y` / `z` |

*Note: Accessing these properties on the CPU (host) will throw a `GpuOnlyException`.*

## 7.2 System.Math Mapping

Standard .NET `System.Math` functions are natively intercepted by the AOT transpiler and mapped to their equivalent CUDA math functions by converting the method name to lowercase.

| C# Construct | CUDA Mapping |
| :--- | :--- |
| `Math.Max(a, b)` | `max(a, b)` |
| `Math.Min(a, b)` | `min(a, b)` |
| `Math.Abs(a)` | `abs(a)` |
| `Math.Sqrt(a)` | `sqrt(a)` |

*Note: Overloads mapping to single-precision (float) or double-precision (double) in C# will automatically map to the equivalent CUDA overload during NVCC compilation.*

## 7.3 Synchronization and Atomics

The `Gpu` static class provides essential synchronization primitives and atomic operations.

* **`Gpu.SyncThreads()`**: Maps to `__syncthreads()`. Creates a barrier synchronization point for all threads within a block.
* **`Gpu.AtomicAdd(ref T address, T val)`**: Maps to `atomicAdd(&address, val)`. Performs an atomic read-modify-write operation to prevent race conditions on shared or global memory.

## 7.4 Multi-Precision Arithmetic (PTX Optimization)

The `Gpu` static class also provides highly optimized multi-precision arithmetic functions. These are transpiled into custom prelude functions (`__gsharp_add_cc`, `__gsharp_sub_cc`) that utilize `unsigned long long` casting to coax the NVCC compiler into emitting highly efficient PTX instructions (`add.cc`, `addc.cc`, `sub.cc`, `subc.cc`).

* **`Gpu.AddWithCarry(uint a, uint b, ref ulong carry)`**: Adds two 32-bit integers with a 64-bit carry in/out. Maps to `__gsharp_add_cc`.
* **`Gpu.SubWithBorrow(uint a, uint b, ref long borrow)`**: Subtracts two 32-bit integers with a 64-bit borrow in/out. Maps to `__gsharp_sub_cc`.

## 7.5 Shared Memory (`SharedMemory<T>`)

The `SharedMemory<T>` struct is a C# `ref struct` that abstracts CUDA's `__shared__` memory space. 

### 7.4.1 Static Shared Memory
If instantiated with a compile-time constant size, the transpiler emits a static shared memory declaration. The semantic model automatically evaluates constant arguments and mathematical expressions (AST evaluation), enabling the use of both explicit arrays, Target-typed `new` (`new()`), and formulas involving `const` variables or `[Constant]` parameters.
```csharp
// Context: G# Kernel (GPU)
SharedMemory<int> smem1 = new SharedMemory<int>(256);
SharedMemory<int> smem2 = new(256); // Target-typed new is fully supported
```
**Transpiled Output:**
```cpp
__shared__ int smem[256];
```

### 7.4.2 Dynamic Shared Memory
If instantiated with a dynamic variable (or a parameter not marked as `[Constant]`), the transpiler utilizes a highly sophisticated dynamic shared memory allocator. Because CUDA restricts a kernel to a single `extern __shared__` array, G# generates a single byte buffer (`__gsharp_dynamic_smem`) and automatically manages aligned pointer arithmetic (`__smem_offset`) to allow multiple dynamic `SharedMemory<T>` instantiations in the same kernel.

```csharp
// Context: G# Kernel (GPU)
SharedMemory<int> smem1 = new SharedMemory<int>(size1);
SharedMemory<float> smem2 = new SharedMemory<float>(size2);
```
**Transpiled Output:**
```cpp
extern __shared__ char __gsharp_dynamic_smem[];
size_t __smem_offset = 0;
__smem_offset = (__smem_offset + alignof(int) - 1) & ~(alignof(int) - 1);
int* smem1 = (int*)(__gsharp_dynamic_smem + __smem_offset);
__smem_offset += (size1) * sizeof(int);

__smem_offset = (__smem_offset + alignof(float) - 1) & ~(alignof(float) - 1);
float* smem2 = (float*)(__gsharp_dynamic_smem + __smem_offset);
__smem_offset += (size2) * sizeof(float);
```
*When using dynamic shared memory, the host launcher must supply the total allocated byte size as the `sharedMemBytes` argument during `cuLaunchKernel` execution.*

### 7.4.3 Limitations
* Because `SharedMemory<T>` transpiles directly to a primitive C array, bounds checking is completely absent on the GPU.
* Attempting to index `SharedMemory<T>` out of bounds will result in silent memory corruption or an illegal memory access fault (`CUDA_ERROR_ILLEGAL_ADDRESS`).

## 7.6 Inline PTX

To allow maximum hardware-level optimization, developers can inject raw PTX or CUDA C++ strings directly into the generated source code using `Intrinsics.InlinePtx(string)`.

```csharp
// Context: G# Kernel (GPU)
uint result = 0;
Intrinsics.InlinePtx("asm volatile (\"mov.u32 %0, %1;\" : \"=r\"(result) : \"r\"(ThreadIndex.X));");
```
*Note: The transpiler emits the string exactly as provided. It is the developer's responsibility to ensure the PTX syntax is correct and valid for the target CUDA architecture.*

## 7.7 Cooperative Kernels

G# supports CUDA Cooperative Groups, allowing Grid-level synchronization across multiple thread blocks. This is essential for massive parallel reductions or algorithms like NTT.

To enable this feature, decorate the kernel with `[GpuKernel(Cooperative = true)]` and utilize `Gpu.SyncGrid()`:

```csharp
// Context: G# Kernel (GPU)
[GpuKernel(Cooperative = true)]
public static void MyCooperativeKernel()
{
    // ... block-level work ...
    Gpu.SyncGrid(); // Synchronizes all threads across all blocks in the grid
    // ... grid-level work ...
}
```
*Note: Under the hood, this instructs the launcher stub to use `cuLaunchCooperativeKernel` instead of `cuLaunchKernel`, and `Gpu.SyncGrid()` is transpiled into the appropriate `cooperative_groups::this_grid().sync()` call in C++.*
