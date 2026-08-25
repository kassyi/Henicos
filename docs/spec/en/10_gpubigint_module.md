# 9. GpuBigInt Module (Kassyi.GSharp.GpuBigInt)

The Kassyi.GSharp.GpuBigInt module provides high-performance multi-precision arithmetic on the GPU. It is designed to handle extremely large integers (e.g., millions of bits) primarily for specialized mathematical workloads like Fermat Primality Testing (Lucas-Lehmer test) and Great Internet Mersenne Prime Search (GIMPS) related algorithms.

## 9.1 Supported Operations

GpuBigInt supports a comprehensive set of multi-precision arithmetic operations, heavily optimized for SIMD architectures:

- **Addition (Add)**: Ripple-carry addition across multiple chunks using Gpu.AddWithCarry.
- **Subtraction (Sub)**: Borrow-based subtraction across multiple chunks using Gpu.SubWithBorrow.
- **Multiplication (Mul)**: Highly optimized multiplication using NTT/FFT techniques for massive sizes.
- **Division (Div)**: Approximate division using Newton-Raphson iteration for reciprocals. **Note:** Due to iterative kernel dispatch overhead, GPU division is relatively slow for small numbers. It is recommended primarily for massive integers exceeding 1,024 words (32,768 bits). For smaller numbers, CPU-based division is generally faster.
- **Square Root (Sqrt)**: Integer square root computation using Heron's method.
- **Bitwise Operations (BitwiseAnd, BitwiseOr, BitwiseXor, BitwiseNot)**: Word-by-word bitwise AND, OR, XOR, NOT operations. Supported in expression trees via `&`, `|`, `^`, `~`.
- **Greatest Common Divisor (Gcd)**: Multi-precision Greatest Common Divisor computation using GPU Lehmer's GCD algorithm.
- **Modular Exponentiation (ModPow)**: Binary exponentiation $a^b \pmod m$ on GPU.
- **Bit Shifts (ShiftLeft, ShiftRight)**: Arbitrary bit shifts across the entire multi-precision array. Both `<<` and `>>` are supported via expression trees.
- **Square (Square)**: Highly optimized squaring using Number Theoretic Transform (NTT) / Fast Fourier Transform (FFT) techniques to handle massive sizes efficiently.
- **Fermat PRP Loop (DynamicFermatLoop, StaticFermatLoop)**: Dedicated loops for computing $S_{i+1} = (S_i^2 - 2) \mod (2^p - 1)$ directly on the GPU.

### Example Usage

```csharp
// Context: Host C# (CPU)
using Kassyi.GSharp.GpuBigInt;

// Create 1024-word (32768-bit) BigIntegers on the GPU
using var gpuA = new GpuBigInteger(1024);
using var gpuB = new GpuBigInteger(1024);
using var gpuResult = new GpuBigInteger(1024);

// ... populate gpuA and gpuB via gpuA.CopyFromHost(...) ...

// Perform massive arithmetic directly on the GPU
GpuBigInteger.Add(gpuA, gpuB, gpuResult);
GpuBigInteger.ShiftLeft(gpuResult, 256, gpuResult);
GpuBigInteger.Div(gpuA, gpuB, gpuResult);

gpuResult.Square(1024); // Highly optimized NTT/FFT squaring

// Retrieve the result back to host memory
uint[] hostResult = gpuResult.ToHostArray();
```

## 9.2 Dual-Engine Architecture

The module utilizes a highly decoupled architecture with a **Dual-Engine Execution Model** (`GpuExecutionEngine`). The physical file structure is divided into specific domains (`Kernels/`, `Providers/`, `Contexts/`, `Interop/`) to maintain modularity:

1.  **C# GpuBigInteger (Host Wrapper)**: A `IDisposable` C# class that manages the `IntPtr` to the GPU device memory. It provides the high-level API for the host application to allocate, copy, and invoke operations.
2.  **Native C++ Backend (`GpuExecutionEngine.LegacyNative`)**: Handled via `Providers/NativeProvider.cs` and `Interop/NativeMethods.cs`, this exposes the legacy hand-written CUDA C++ library for memory management (`cudaMalloc`) and executing NTT/FFT kernels.
3.  **G# Kernels (`GpuExecutionEngine.GSharpJit`)**: Handled via `Providers/GSharpJitProvider.cs`, this is a pure C# JIT backend where the mathematical operations are natively written in C# using `[GpuKernel]` inside the `Kernels/` directory (e.g., `GpuBigIntFermatKernels.cu.cs`, `GpuBigIntMulKernels.cu.cs`). **By convention, all C# files containing G# GPU kernels must use the `.cu.cs` file extension.** In this mode, the managed code directly performs CUDA allocations (`cuMemAlloc_v2`) and dispatches the kernels dynamically, bypassing the C++ DLL completely.

_Complex kernel signatures are neatly encapsulated by utilizing `ref struct` wrappers (e.g., `ref FermatContext` inside `Contexts/`) powered by the Deep Struct Marshaling engine, eliminating the need to pass dozens of arrays manually._

### 9.2.1 GpuContext & Workspace Memory Management

Because initializing large buffers for big integers on the GPU is expensive, `GpuBigInt` requires a `GpuContext` object.
```csharp
// Context: Host C# (CPU)
using var context = new GpuContext(MaxWords, GpuExecutionEngine.GSharpJit);
```
The context routes execution to the specified engine and performs the following roles:
* Manages a `WorkspacePointer` (`IntPtr`), entirely eliminating GPU global state. This makes parallel processing completely thread-safe across multiple CUDA contexts.
* Pre-allocates working buffers within the workspace to avoid `cudaMalloc` overhead during computation.
* Provides a `RentTemp` object pool to handle intermediate values (`GpuBigIntExpr`) dynamically without creating garbage.

## 9.3 Lazy Evaluation & Operator Overloading (Expression Trees)

To provide highly readable syntax without sacrificing GPU memory allocation performance (`cudaMalloc` overhead), `GpuBigInt` employs a **Lazy Evaluation / Computational Graph** architecture.

Instead of computing immediately, operator overloads (`+`, `-`, `*`, `/`, `<<`, `>>`, `&`) and static methods like `GpuBigIntExpr.Sqrt()` construct an AST (Abstract Syntax Tree) representing the mathematical operations:

> [!WARNING]
> **Host-Side Only API**: These operator overloads and expression trees are designed exclusively for host-side orchestration. You **cannot** use `+`, `-`, `*`, `/` or `Sqrt()` on `GpuBigInteger` objects from *inside* a `[GpuKernel]` method. All `GpuBigInt` arithmetic operations involve orchestrating multiple low-level CUDA kernels and workspace memory pools, which must be dispatched and managed from the CPU host.

```csharp
// Context: Host C# (CPU)
using Kassyi.GSharp.GpuBigInt.Expressions;

// ❌ INCORRECT: Triggers hard compiler error GSH0005
// You cannot assign an expression to a local variable (prevents "computation lost" bugs).
// var recipe = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA; // Error!

// ✨ CORRECT: Evaluate the tree and write the result directly into a pre-allocated buffer
gpuResult.Value = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;
```

### 9.3.1 Diagnostic GSH0005: Preventing Computation Loss

Because operators only build an AST and do not execute any kernels natively, failing to assign the resulting expression to a `.Value` property leads to a silent "computation lost" bug (the GPU kernel is never dispatched).

To prevent this, the G# Roslyn Analyzer introduces `GSH0005` (GpuBigIntExpr Direct Assignment). This diagnostic produces a **hard compiler error** if a `GpuBigIntExpr` is declared or assigned to a local variable (e.g., `var x = a + b;`) without being resolved directly into a physical `GpuBigInteger.Value`. This enforces strict compile-time safety and forces developers to write single-line assignments (`gpuResult.Value = a + b;`).

### Execution Strategy

When `.Value` is assigned, the runtime analyzes the expression tree and executes it using an optimized execution plan:

1. **Buffer Pooling**: Intermediate results are routed to a pre-allocated pool of temporary `GpuBigInteger` buffers managed by `GpuContext`. This reduces `cudaMalloc` allocations to **zero** during tight computational loops.
2. **Kernel Fusion (Future)**: The execution engine may dynamically generate and JIT-compile fused CUDA kernels (combining operations like Add and And into a single memory pass) to minimize global memory bandwidth.

## 9.4 Benchmarking (Henicos.PiBenchmark)

To validate and measure the high-load performance of the `GpuBigInt` module, the `Henicos.PiBenchmark` project is provided. This project executes massive multi-precision math (such as calculating millions of digits of Pi using Chudnovsky's algorithm or similar formulas) entirely on the GPU.

By isolating the heavy arithmetic to `GpuBigInt` expressions, the benchmark serves as a practical, real-world stress test for NTT multiplication, Newton-Raphson division, and memory pooling efficiencies, proving the module's capability to scale to extreme sizes.

## 9.5 GPU Debugging Best Practices (CPU-First / TDD Workflow)

Debugging complex mathematical algorithms directly within GpuBigInt kernels (e.g., .cu.cs) is exceptionally difficult due to the lack of GPU breakpoints and obscured JIT transpile errors. When implementing advanced algorithms like NTT or GCD, strictly adhere to the following workflow:

1. **CPU Reference Implementation**: Implement the mathematical logic entirely in standard C# within a temporary CPU-side scratch project. This validates constants (e.g., primitive roots, modulo invariants) and proves mathematical correctness.
2. **NVCC Logs Verification**: If a GPU test fails or hangs unexpectedly, do not assume the math is flawed immediately. Check the background 	ask-xxx.log (NVCC compilation logs) to ensure the Roslyn C#-to-CUDA transpiler hasn't silently dropped a method due to syntax constraints (e.g., using => or braceless if).
3. **Migrate to Device Code**: Once the CPU code is mathematically sound and unit-tested, introduce [GpuDevice] / [GpuKernel] attributes and deploy it to the GPU. Ensure you do not use [GpuKernel] on internal subroutines, as this will break CUDA's function linkage.
