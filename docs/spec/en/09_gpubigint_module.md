# 9. GpuBigInt Module (Kassyi.GSharp.GpuBigInt)

The Kassyi.GSharp.GpuBigInt module provides high-performance multi-precision arithmetic on the GPU. It is designed to handle extremely large integers (e.g., millions of bits) primarily for specialized mathematical workloads like Fermat Primality Testing (Lucas-Lehmer test) and Great Internet Mersenne Prime Search (GIMPS) related algorithms.

## 9.1 Supported Operations

GpuBigInt does **not** support general-purpose four-function arithmetic (General Multiplication, Division, or Modulo) due to the immense complexity and divergence of implementing these for arbitrary lengths on SIMD architectures.

Instead, it supports a highly specialized subset of operations:

- **Addition (Add)**: Ripple-carry addition across multiple chunks using Gpu.AddWithCarry.
- **Subtraction (Sub)**: Borrow-based subtraction across multiple chunks using Gpu.SubWithBorrow.
- **Bitwise AND (BitwiseAnd)**: Word-by-word bitwise AND.
- **Bit Shifts (ShiftLeft, ShiftRight)**: Arbitrary bit shifts across the entire multi-precision array.
- **Square (Square)**: Highly optimized squaring. In the native C++ backend, this is often implemented using Number Theoretic Transform (NTT) / Fast Fourier Transform (FFT) techniques (e.g., GpuBigInt_NTT.cuh) to handle the massive sizes efficiently.
- **Fermat PRP Loop (DynamicFermatLoop, StaticFermatLoop)**: Dedicated loops for computing ${i+1} = (S_i^2 - 2) \mod (2^p - 1)$ directly on the GPU.

### Example Usage

```csharp
using Kassyi.GSharp.GpuBigInt;

// Create 1024-word (32768-bit) BigIntegers on the GPU
using var gpuA = new GpuBigInteger(1024);
using var gpuB = new GpuBigInteger(1024);
using var gpuResult = new GpuBigInteger(1024);

// ... populate gpuA and gpuB via gpuA.CopyFromHost(...) ...

// Perform massive arithmetic directly on the GPU
GpuBigInteger.Add(gpuA, gpuB, gpuResult);
GpuBigInteger.ShiftLeft(gpuResult, 256, gpuResult);
GpuBigInteger.BitwiseAnd(gpuResult, gpuA, gpuResult);

gpuResult.Square(1024); // Highly optimized NTT/FFT squaring

// Retrieve the result back to host memory
uint[] hostResult = gpuResult.ToHostArray();
```

## 9.2 Architecture

The module utilizes a three-layer architecture:

1.  **C# GpuBigInteger (Host Wrapper)**: A IDisposable C# class that manages the IntPtr to the GPU device memory. It provides the high-level API for the host application to allocate, copy, and invoke operations.
2.  **Native C++ Backend (Kassyi.GSharp.GpuBigInt.Cuda.dll)**: A hand-written CUDA C++ library exposing `extern "C"` functions (NativeMethods) that perform the actual memory management (cudaMalloc) and execute the optimized NTT/FFT kernels.
3.  **G# Kernels (GpuBigIntKernels.cs)**: A subset of the logic (like ChunkAddKernel, ApplyAddCarryKernel) has been rewritten natively in C# using [GpuKernel]. These kernels use the Kassyi.GSharp.Core intrinsics (e.g., Gpu.AddWithCarry) and are transpiled to PTX on the fly.

_Future phases of the G# project aim to port the NTT/FFT Square and FermatLoop implementations entirely into GpuBigIntKernels.cs, eventually deprecating the Native C++ Backend entirely._

## 9.3 Lazy Evaluation & Operator Overloading (Expression Trees)

To provide highly readable syntax without sacrificing GPU memory allocation performance (`cudaMalloc` overhead), `GpuBigInt` employs a **Lazy Evaluation / Computational Graph** architecture.

Instead of computing immediately, operator overloads (`+`, `-`, `<<`, `&`) construct an AST (Abstract Syntax Tree) representing the mathematical operations:

```csharp
// 1. Builds an expression tree, DOES NOT allocate memory or compute
var recipe = ((gpuA + gpuB) << 256) & gpuA;

// 2. Evaluates the tree into a pre-allocated result buffer
gpuResult.Evaluate(recipe);
```

### Execution Strategy

When `Evaluate()` is called, the runtime analyzes the expression tree and executes it using an optimized execution plan:

1. **Buffer Pooling**: Intermediate results are routed to a pre-allocated pool of temporary `GpuBigInteger` buffers managed by `GpuContext`. This reduces `cudaMalloc` allocations to **zero** during tight computational loops.
2. **Kernel Fusion (Future)**: The execution engine may dynamically generate and JIT-compile fused CUDA kernels (combining operations like Add and And into a single memory pass) to minimize global memory bandwidth.
