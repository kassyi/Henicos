# 3. Arbitrary-Precision GPU Integer Module (GpuBigInt)

The `Kassyi.GSharp.GpuBigInt` module provides high-performance multi-precision arithmetic on the GPU. It is designed to handle extremely large integers (e.g., millions of bits) primarily for specialized mathematical workloads like Fermat Primality Testing (Lucas-Lehmer test) and Great Internet Mersenne Prime Search (GIMPS) related algorithms.

> [!WARNING]
> **Host-Side Only API**: The `GpuBigInteger` class and its operator overloads are designed exclusively for host-side orchestration. You **cannot** use `+`, `-`, `*`, `/` or `Sqrt()` on `GpuBigInteger` objects from *inside* a `[GpuKernel]` method. All `GpuBigInt` arithmetic operations involve orchestrating multiple low-level CUDA kernels and workspace memory pools, which must be dispatched and managed from the CPU host.

---

## Part A: Host API Reference (Host-Only)

### `GpuContext` Class

Manages a thread-safe workspace pointer and pre-allocates intermediate buffers on the GPU to completely eliminate `cudaMalloc` overhead during execution.

**Syntax**
```csharp
public class GpuContext : IDisposable
{
    public GpuContext(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot);
    public void Dispose();
}
```

**Remarks**
* Must be instantiated before allocating any `GpuBigInteger` instances.
* Supports a Dual-Engine architecture (`GSharpAot` for pure C# JIT/AOT kernels, `LegacyNative` for C++ DLLs).

### `GpuUIntBigInteger` Class

A host-wrapper class (`IDisposable`) that manages the `IntPtr` to the GPU device memory for massive unsigned integers.

**Syntax**
```csharp
public class GpuUIntBigInteger : IDisposable
{
    public GpuUIntBigInteger(GpuContext context, int numWords);
    public GpuUIntBigInteger(GpuContext context, uint[] hostData);
    public uint[] ToHostArray();
    public static GpuUIntBigInteger FromBigInteger(GpuContext context, BigInteger value, int minWords = 0);
    public BigInteger ToBigInteger();
    public void Dispose();
}
```

### `GpuBigInteger` Struct

A host-wrapper struct that pairs a `GpuUIntBigInteger` with a sign bit.

**Syntax**
```csharp
public readonly struct GpuBigInteger : IDisposable, IComparable<GpuBigInteger>
{
    public GpuBigInteger(GpuUIntBigInteger value, bool isNegative = false);
    public GpuUIntBigInteger Value { get; }
    public bool IsNegative { get; }
    public void Dispose();
}
```

---

## Part B: Supported Operations & Methods (Host-Only)

*Note: In the complexity analysis below, $N$ represents the number of 32-bit words (limbs) required to store the arbitrary-precision integer.*

### Addition and Subtraction

Performs ripple-carry addition (using `Gpu.AddWithCarry`) or borrow-based subtraction (`Gpu.SubWithBorrow`) across multiple chunks.

**Algorithm & Complexity**
* **Algorithm**: Parallel Ripple-Carry / Borrow Propagation
* **Time Complexity**: $\mathcal{O}(N)$ total work. Highly parallelized across CUDA blocks.

**Operator Overloads**
**(Host-Only):** `GpuBigInt` provides `+` and `-` operator overloads designed exclusively for host-side orchestration. These overloads return a `GpuBigIntExpr` (Expression Tree) for lazy evaluation.

**Example**
```csharp
var expr = gpuA + gpuB - gpuC;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### Multiplication

Highly optimized multiplication for massive sizes.

**Algorithm & Complexity**
* **Algorithm**: Number Theoretic Transform (NTT) / Fast Fourier Transform (FFT)
* **Time Complexity**: $\mathcal{O}(N \log N)$

**Operator Overloads**
**(Host-Only):** The `*` operator overload is available exclusively on the host to construct a lazy-evaluated `GpuBigIntExpr`.

**Example**
```csharp
var expr = gpuA * gpuB;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.Div` (Division)

Performs approximate division using Newton-Raphson iteration for reciprocals.

**Algorithm & Complexity**
* **Algorithm**: Newton-Raphson Iteration (utilizing NTT Multiplication)
* **Time Complexity**: $\mathcal{O}(N \log N)$

**Syntax**
```csharp
public static void Div(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result);
```

**Remarks**
Due to iterative kernel dispatch overhead, GPU division is relatively slow for small numbers. It is recommended primarily for massive integers exceeding 1,024 words (32,768 bits). For smaller numbers, CPU-based division is generally faster.

**Operator Overloads**
**(Host-Only):** The `/` operator is available exclusively on the host to construct a lazy-evaluated `GpuBigIntExpr` for division.

### `GpuBigInteger.Sqrt` (Square Root)

Integer square root computation.

**Algorithm & Complexity**
* **Algorithm**: Heron's Method (Newton-Raphson for Square Roots)
* **Time Complexity**: $\mathcal{O}(N \log N)$

**Syntax**
```csharp
public static GpuBigIntExpr Sqrt(GpuBigInteger a);
```

**Example**
```csharp
var expr = GpuBigInteger.Sqrt(gpuA);
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.Pow10` (Power of 10)

Fast computation of $10^M$, optimized for arbitrary precision decimal scaling.

**Algorithm & Complexity**
* **Algorithm**: Binary Exponentiation
* **Time Complexity**: $\mathcal{O}(N \log N)$ (dominated by NTT multiplication step).

**Syntax**
```csharp
public static GpuBigIntExpr Pow10(int exponent);
```

**Example**
```csharp
var expr = GpuBigInteger.Pow10(100); // Calculates 10^100
using var gpuResult = expr.Node.Evaluate(context, null);
```

### Bitwise Operations and Bit Shifts

Word-by-word bitwise AND, OR, XOR, NOT operations, and arbitrary bit shifts across the entire multi-precision array.

**Algorithm & Complexity**
* **Algorithm**: SIMD Parallel Bitwise Map / Parallel Shift
* **Time Complexity**: $\mathcal{O}(N)$ total work, approaching $\mathcal{O}(1)$ step time due to massive parallelism across CUDA cores.

**Syntax**
```csharp
public static GpuBigIntExpr BitwiseAnd(GpuBigInteger a, GpuBigInteger b);
public static GpuBigIntExpr BitwiseOr(GpuBigInteger a, GpuBigInteger b);
public static GpuBigIntExpr BitwiseXor(GpuBigInteger a, GpuBigInteger b);
public static GpuBigIntExpr BitwiseNot(GpuBigInteger a);
public static GpuBigIntExpr ShiftLeft(GpuBigInteger a, int shiftBits);
public static GpuBigIntExpr ShiftRight(GpuBigInteger a, int shiftBits);
```

**Remarks (Operator Overloads)**
**(Host-Only):** For convenience in building host-side expression trees, `GpuBigInt` implements standard C# operator overloads that transparently map to these methods. These operators cannot be used inside `[GpuKernel]`:
* `a & b` (BitwiseAnd)
* `a | b` (BitwiseOr)
* `a ^ b` (BitwiseXor)
* `~a` (BitwiseNot)
* `a << shiftBits` (ShiftLeft)
* `a >> shiftBits` (ShiftRight)

**Example**
```csharp
// Easily composed via standard C# operators using Expression Trees
var expr = (~gpuA & gpuB) << 256;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.Gcd` (Greatest Common Divisor)

Multi-precision Greatest Common Divisor computation.

**Algorithm & Complexity**
* **Algorithm**: Lehmer's GCD algorithm optimized for GPU execution
* **Time Complexity**: $\mathcal{O}(N^2)$ (standard Lehmer) to $\mathcal{O}(N \log^2 N)$ (subquadratic execution paths).

**Syntax**
```csharp
public static GpuBigIntExpr Gcd(GpuBigInteger a, GpuBigInteger b);
```

**Example**
```csharp
var expr = GpuBigInteger.Gcd(gpuA, gpuB);
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.ModPow` (Modular Exponentiation)

Binary exponentiation $a^b \pmod m$ on GPU.

**Algorithm & Complexity**
* **Algorithm**: Binary Exponentiation with Montgomery or Barrett Reduction
* **Time Complexity**: $\mathcal{O}(K \cdot N \log N)$ where $K$ is the bit-length of the exponent $b$.

**Syntax**
```csharp
public static GpuBigIntExpr ModPow(GpuBigInteger a, GpuBigInteger exponent, GpuBigInteger modulus);
```

**Example**
```csharp
var expr = GpuBigInteger.ModPow(gpuA, exponent, modulus);
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.Square` (Instance Method)

Highly optimized in-place squaring.

**Algorithm & Complexity**
* **Algorithm**: Number Theoretic Transform (NTT) / Fast Fourier Transform (FFT)
* **Time Complexity**: $\mathcal{O}(N \log N)$

**Syntax**
```csharp
public void Square(int precisionWords);
```

**Remarks**
This is an in-place mutation operation. For extremely large integers, calling `Square()` directly is significantly faster than using the standard multiplication operator (`A * A`) via expression trees.

**Example**
```csharp
gpuA.Value.Square(1024); // Squares gpuA's underlying unsigned value in place
```

### Fermat PRP Loop (DynamicFermatLoop, StaticFermatLoop)

Dedicated loops for computing $S_{i+1} = (S_i^2 - 2) \mod (2^p - 1)$ directly on the GPU.

**Algorithm & Complexity**
* **Algorithm**: Lucas-Lehmer Test (NTT Squaring + Fast Mersenne Modulo)
* **Time Complexity**: $\mathcal{O}(\text{iterations} \cdot N \log N)$

**Syntax**
```csharp
public static void DynamicFermatLoop(GpuBigInteger value, int iterations, int p);
```

**Remarks**
These kernels encapsulate the entire Lucas-Lehmer test loop within the device to avoid CPU-GPU roundtrips.

### BBP Binary-First Verification

Implements the BBP formula verification logic optimized for binary-first extraction on the GPU.

**Algorithm & Complexity**
* **Algorithm**: BBP Formula with Modular Exponentiation
* **Time Complexity**: $\mathcal{O}(N \log N \cdot \log(\text{hex digits}))$

**Remarks**
This specialized method verifies digits generated by the BBP algorithm natively on the device, accelerating Pi verification phases.

---

## Part C: Operator Overloading & Expression Trees (Host-Only)

To provide highly readable syntax without sacrificing GPU memory allocation performance (`cudaMalloc` overhead), `GpuBigInt` employs a **Lazy Evaluation / Computational Graph** architecture.

Instead of computing immediately, operator overloads (`+`, `-`, `*`, `/`, `<<`, `>>`, `&`) construct an AST (Abstract Syntax Tree).

### Supported Operator Overloads
* **Arithmetic**: `+`, `-`, `*` (NTT/FFT), `/` (Newton-Raphson)
* **Bitwise**: `&`, `|`, `^`, `~`
* **Bit Shifts**: `<<`, `>>`

**Example: Comprehensive Expression Tree**
```csharp
// Evaluate the tree and return the result as a new buffer
var expr = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### Diagnostic GSH0005: Preventing Computation Loss

Because operators only build an AST and do not execute any kernels natively, failing to evaluate the resulting expression leads to a silent "computation lost" bug (the GPU kernel is never dispatched).

To prevent this, the G# Roslyn Analyzer introduces `GSH0005` (GpuBigIntExpr Direct Assignment). This diagnostic produces a **hard compiler error** if a `GpuBigIntExpr` is declared or assigned to a local variable without being resolved or evaluated.

```csharp
// ❌ INCORRECT: Triggers hard compiler error GSH0005
// var recipe = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;

// ✨ CORRECT: Enforced evaluation
var expr = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### Execution Strategy

When `.Evaluate()` is called, the runtime analyzes the expression tree and executes it using an optimized execution plan:

1. **Buffer Pooling**: Intermediate results are routed to a pre-allocated pool of temporary buffers managed by `GpuContext`. This reduces `cudaMalloc` allocations to **zero** during tight computational loops.
2. **Kernel Fusion (Future)**: The execution engine may dynamically generate and JIT-compile fused CUDA kernels (combining operations like Add and And into a single memory pass) to minimize global memory bandwidth.

---

## Part D: Internal Architecture and Debugging

### Dual-Engine Architecture

The module utilizes a highly decoupled architecture with a **Dual-Engine Execution Model** (`GpuExecutionEngine`). The physical file structure is divided into specific domains (`Kernels/`, `Providers/`, `Contexts/`, `Interop/`) to maintain modularity:

1.  **C# GpuBigInteger (Host Wrapper)**: A `IDisposable` C# class that manages the `IntPtr` to the GPU device memory. It provides the high-level API for the host application to allocate, copy, and invoke operations.
2.  **Native C++ Backend (`GpuExecutionEngine.LegacyNative`)**: Handled via `Providers/NativeProvider.cs` and `Interop/NativeMethods.cs`, this exposes the legacy hand-written CUDA C++ library for memory management (`cudaMalloc`) and executing NTT/FFT kernels.
3.  **G# Kernels (`GpuExecutionEngine.GSharpAot`)**: Handled via `Providers/GSharpAotProvider.cs`, this is a pure C# JIT backend where the mathematical operations are natively written in C# using `[GpuKernel]` inside the `Kernels/` directory (e.g., `GpuBigIntFermatKernels.cu.cs`, `GpuBigIntMulKernels.cu.cs`). **By convention, all C# files containing G# GPU kernels must use the `.cu.cs` file extension.** In this mode, the managed code directly performs CUDA allocations (`cuMemAlloc_v2`) and dispatches the kernels dynamically, bypassing the C++ DLL completely.

_Complex kernel signatures are neatly encapsulated by utilizing `ref struct` wrappers (e.g., `ref FermatContext` inside `Contexts/`) powered by the Deep Struct Marshaling engine, eliminating the need to pass dozens of arrays manually._

### GpuContext & Workspace Memory Management (Internal)
The context routes execution to the specified engine and performs the following roles:
* Manages a `WorkspacePointer` (`IntPtr`), entirely eliminating GPU global state. This makes parallel processing completely thread-safe across multiple CUDA contexts.
* Pre-allocates working buffers within the workspace to avoid `cudaMalloc` overhead during computation.
* Provides a `RentTemp` object pool to handle intermediate values (`GpuBigIntExpr`) dynamically without creating garbage.

### Benchmarking (Henicos.PiBenchmark)

To validate and measure the high-load performance of the `GpuBigInt` module, the `Henicos.PiBenchmark` project is provided. This project executes massive multi-precision math (such as calculating millions of digits of Pi using Chudnovsky's algorithm or similar formulas) entirely on the GPU.

By isolating the heavy arithmetic to `GpuBigInt` expressions, the benchmark serves as a practical, real-world stress test for NTT multiplication, Newton-Raphson division, and memory pooling efficiencies, proving the module's capability to scale to extreme sizes.

### GPU Debugging Best Practices (CPU-First / TDD Workflow)

Debugging complex mathematical algorithms directly within GpuBigInt kernels (e.g., .cu.cs) is exceptionally difficult due to the lack of GPU breakpoints and obscured JIT transpile errors. When implementing advanced algorithms like NTT or GCD, strictly adhere to the following workflow:

1. **CPU Reference Implementation**: Implement the mathematical logic entirely in standard C# within a temporary CPU-side scratch project. This validates constants (e.g., primitive roots, modulo invariants) and proves mathematical correctness.
2. **NVCC Logs Verification**: If a GPU test fails or hangs unexpectedly, do not assume the math is flawed immediately. Check the background `ask-xxx.log` (NVCC compilation logs) to ensure the Roslyn C#-to-CUDA transpiler hasn't silently dropped a method due to syntax constraints (e.g., using `=>` or braceless `if`).
3. **Migrate to Device Code**: Once the CPU code is mathematically sound and unit-tested, introduce `[GpuDeviceFunction]` / `[GpuKernel]` attributes and deploy it to the GPU. Ensure you do not use `[GpuKernel]` on internal subroutines, as this will break CUDA's function linkage.

### Advanced Standard Library Algorithms (`Kassyi.GSharp.Stdlib.Math`)

In addition to core arithmetic, the standard library includes specialized high-performance math structures designed for GPU execution:

* **PQT Structures (Hypergeometric Series)**: Data structures (`PQT`) for evaluating hypergeometric series natively on the GPU. These are essential for algorithms like Chudnovsky's Pi calculation.
* **GPU Radix Converter**: Fast, parallelized radix conversion algorithms (e.g., base-2 to base-10) for extremely large numbers, removing the CPU bottleneck when formatting millions of digits for output.
