# 7. Intrinsics and Built-ins (`Kassyi.GSharp.Core`)

This document details the core intrinsic APIs and dummy structures provided by `Kassyi.GSharp.Core`. These constructs provide C# IntelliSense support during development but are dynamically intercepted and replaced by the AOT transpiler with raw CUDA equivalents.

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
If instantiated with a compile-time constant size, the transpiler emits a static shared memory declaration.
```csharp
SharedMemory<int> smem = new SharedMemory<int>(256);
```
**Transpiled Output:**
```cpp
__shared__ int smem[256];
```

### 7.4.2 Dynamic Shared Memory
If instantiated with a dynamic variable (or a parameter not marked as `[JitConstant]`), the transpiler utilizes a highly sophisticated dynamic shared memory allocator. Because CUDA restricts a kernel to a single `extern __shared__` array, G# generates a single byte buffer (`__gsharp_dynamic_smem`) and automatically manages aligned pointer arithmetic (`__smem_offset`) to allow multiple dynamic `SharedMemory<T>` instantiations in the same kernel.

```csharp
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
