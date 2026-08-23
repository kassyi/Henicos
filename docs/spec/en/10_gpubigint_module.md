# 9. GpuBigInt Module (Kassyi.GSharp.GpuBigInt)

The Kassyi.GSharp.GpuBigInt module provides high-performance multi-precision arithmetic on the GPU. It is designed to handle extremely large integers (e.g., millions of bits) primarily for specialized mathematical workloads like Fermat Primality Testing (Lucas-Lehmer test) and Great Internet Mersenne Prime Search (GIMPS) related algorithms.

## 9.1 Supported Operations

GpuBigInt does **not** support general-purpose four-function arithmetic (General Multiplication, Division, or Modulo) due to the immense complexity and divergence of implementing these for arbitrary lengths on SIMD architectures.

Instead, it supports a highly specialized subset of operations:

- **Addition (Add)**: Ripple-carry addition across multiple chunks using Gpu.AddWithCarry.
- **Subtraction (Sub)**: Borrow-based subtraction across multiple chunks using Gpu.SubWithBorrow.
- **Bitwise AND (BitwiseAnd)**: Word-by-word bitwise AND.
- **Bit Shifts (ShiftLeft, ShiftRight)**: Arbitrary bit shifts across the entire multi-precision array. Both `<<` and `>>` are supported via expression trees.
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

## 9.2 Dual-Engine Architecture

The module utilizes a three-layer architecture and supports a **Dual-Engine Execution Model** (`GpuExecutionEngine`):

1.  **C# GpuBigInteger (Host Wrapper)**: A `IDisposable` C# class that manages the `IntPtr` to the GPU device memory. It provides the high-level API for the host application to allocate, copy, and invoke operations.
2.  **Native C++ Backend (`GpuExecutionEngine.LegacyNative`)**: The legacy, hand-written CUDA C++ library exposing `extern "C"` functions (`NativeMethods`) that perform the actual memory management (`cudaMalloc`) and execute optimized NTT/FFT kernels.
3.  **G# Kernels (`GpuExecutionEngine.GSharpJit`)**: A pure C# JIT backend where the mathematical operations (`ChunkAddKernel`, `ApplyAddCarryKernel`, etc.) are natively rewritten in C# using `[GpuKernel]`. In this mode, the managed code directly performs CUDA allocations (`cuMemAlloc_v2`) and dispatches the kernels dynamically, bypassing the C++ DLL completely.

_The NTT/FFT Squaring and Fermat PRP Loops have been successfully ported entirely into pure C# kernels (`GpuBigIntFermatKernels.cs`). Complex kernel signatures are neatly encapsulated by utilizing `ref struct` wrappers (e.g., `ref FermatContext`) powered by the Deep Struct Marshaling engine, eliminating the need to pass dozens of arrays manually._

### 9.2.1 GpuContext & Workspace Memory Management

Because initializing large buffers for big integers on the GPU is expensive, `GpuBigInt` requires a `GpuContext` object.
```csharp
using var context = new GpuContext(MaxWords, GpuExecutionEngine.GSharpJit);
```
The context routes execution to the specified engine and performs the following roles:
* Manages a `WorkspacePointer` (`IntPtr`), entirely eliminating GPU global state. This makes parallel processing completely thread-safe across multiple CUDA contexts.
* Pre-allocates working buffers within the workspace to avoid `cudaMalloc` overhead during computation.
* Provides a `RentTemp` object pool to handle intermediate values (`GpuBigIntExpr`) dynamically without creating garbage.

## 9.3 Lazy Evaluation & Operator Overloading (Expression Trees)

To provide highly readable syntax without sacrificing GPU memory allocation performance (`cudaMalloc` overhead), `GpuBigInt` employs a **Lazy Evaluation / Computational Graph** architecture.

Instead of computing immediately, operator overloads (`+`, `-`, `<<`, `>>`, `&`) construct an AST (Abstract Syntax Tree) representing the mathematical operations:

```csharp
// 1. Builds an expression tree, DOES NOT allocate memory or compute
var recipe = ((gpuA + gpuB) << 256) & gpuA;

// 2. Evaluates the tree and writes the result into a pre-allocated buffer
gpuResult.Value = recipe;
```

### 9.3.1 Diagnostic GSH0005: Preventing Computation Loss

Because operators only build an AST and do not execute any kernels natively, failing to assign the resulting expression to a `.Value` property leads to a silent "computation lost" bug (the GPU kernel is never dispatched).

To prevent this, the G# Roslyn Analyzer introduces `GSH0005` (GpuBigIntExpr Direct Assignment). This diagnostic produces a compiler error if a `GpuBigIntExpr` is declared or assigned to a local variable (`var x = a + b;`) without being resolved into a physical `GpuBigInteger.Value`, ensuring strict compile-time safety.

### Execution Strategy

When `.Value` is assigned, the runtime analyzes the expression tree and executes it using an optimized execution plan:

1. **Buffer Pooling**: Intermediate results are routed to a pre-allocated pool of temporary `GpuBigInteger` buffers managed by `GpuContext`. This reduces `cudaMalloc` allocations to **zero** during tight computational loops.
2. **Kernel Fusion (Future)**: The execution engine may dynamically generate and JIT-compile fused CUDA kernels (combining operations like Add and And into a single memory pass) to minimize global memory bandwidth.
