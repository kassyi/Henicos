---
title: "4.12 GPU LINQ & Execution Guide for C# Developers"
site: "G# Documentation"
published: "2026-10-12T00:00:00.000Z"
language: "en"
description: "A comprehensive guide bridging standard C# LINQ concepts to GPU SIMT execution models, detailing AsStackEnumerable, AsParallel, AsVectorizedEnumerable, and CUDA Graph evaluation."
---

# 4.12 GPU LINQ & Execution Guide for C# Developers

## 1. Why Does G# Provide Multiple LINQ Pipelines and Execution Methods?

Standard C# (`IEnumerable<T>` and LINQ) completely abstracts the underlying execution hardware (CPU registers, cache lines, SIMD intrinsics, and multi-threading), embracing the philosophy that "all data pipelines look uniform."

However, enforcing this total abstraction on GPUs leads to catastrophic performance degradation. In GPU computing, three physical realities govern performance:
1. **Thread Count & Coordination**: Whether an operation is performed by a single thread, distributed across SIMT lanes, or cooperatively aggregated via shared memory.
2. **Memory Coalescing**: Whether consecutive threads access contiguous global memory words simultaneously.
3. **Kernel Launch Latency**: The CPU-to-GPU PCIe dispatch overhead incurred when issuing individual device kernels.

G# retains the familiarity of C# LINQ syntax while intentionally separating APIs into specialized pipelines and execution strategies. This allows developers to explicitly select the optimal hardware execution model.

---

## 2. Data Access (LINQ) Pipelines and Threading Models

The first stage of designing a GPU query is deciding how elements are accessed and how threads are dispatched across the device grid:

| G# API Pipeline | C# / .NET Analogy | GPU Execution & Threading Model | Return Type |
| :--- | :--- | :--- | :--- |
| `AsStackEnumerable()`<br>*(Terminated with `.Sum()`, etc.)* | Standard sequential `IEnumerable<T>` | **Single Thread (Sequential)**<br>A single GPU thread executes a `for` loop sequentially across array elements. Incurs high latency on large datasets. | Scalar expression<br>(`GpuBigIntExpr`) |
| `AsStackEnumerable()`<br>`.AsParallel()`<br>*(Terminated with `.Sum()`, etc.)* | PLINQ (`AsParallel()`) | **Full-Grid Cooperative (Shared Memory)**<br>Work is partitioned across all threads in the grid. Threads cooperate via block-level shared memory to compute tree reductions. | Scalar expression<br>(`GpuBigIntExpr`) |
| `AsVectorizedEnumerable()` | `Vector<T>` / `Vector256<T>` *(SIMD)* | **Full-Grid Independent (SIMD / Coalesced)**<br>Every thread operates on its assigned element in contiguous memory without cross-thread coordination or branching. | Vector expression<br>(`IGpuBigIntVectorExpression`) |

---

## 3. Capabilities, Constraints, and Code Examples

Different GPU pipelines enforce specific constraints driven by hardware physics (such as warp divergence and coalesced memory access).

### 3.1 Scalar Sequential Aggregation (`AsStackEnumerable`)
* **Capabilities**: Supports arbitrary, complex filtering (`Where`) and projections (`Select`) via compile-time AST transpilation.
* **Constraints**: Executes on a single GPU thread by default; not intended for massive datasets without parallelization.

```csharp
// Supported: Filters even numbers, doubles them, and sums sequentially on a single GPU thread
var sumExpr = array
    .AsStackEnumerable()
    .Where(x => x % 2 == 0)
    .Select(x => x * 2)
    .Sum();

GpuBigInt result = sumExpr.Evaluate();
```

### 3.2 Parallel Reduction Aggregation (`AsParallel`)
* **Capabilities**: Drastically accelerates reductions (`Sum`, `Max`, `Min`, `Aggregate`) over massive collections using shared memory.
* **Constraints**: Intermediate transformations (`Where`, `Select`) must precede `.AsParallel()`. The pipeline following `.AsParallel()` must be terminated with a reduction.

```csharp
// Supported: Filters even numbers, then executes parallel shared-memory reduction across the GPU grid
var parallelSumExpr = array
    .AsStackEnumerable()
    .Where(x => x % 2 == 0)
    .AsParallel()
    .Sum();

GpuBigInt result = parallelSumExpr.Evaluate();
```

### 3.3 Vectorized Batch Operations (`AsVectorizedEnumerable`)
* **Capabilities**: Enforces coalesced memory streaming for high-throughput batch operations on both `GpuBigIntArray` (`IGpuBigIntVectorExpression`) and `GpuBigFloatArray` (`IGpuBigFloatVectorExpression`), outputting a new device array. Supports scalar batch projections (`SelectAdd`, `SelectSubtract`, `SelectMultiply`, `SelectDivide`, and `SelectModulo` on `GpuBigIntArray`), element-wise zip operations (`ZipAdd`, `ZipSubtract`, `ZipMultiply`, `ZipDivide`, and `ZipModulo` on `GpuBigIntArray`), slicing (`Skip`, `Take`), stream compaction (`Where` on `GpuBigIntArray`), and full-vector reductions (`Sum`, `Max`, `Min`, `Average`, `Count`).

```csharp
// Supported: Multiplies or divides each element by a scalar value in parallel (SIMD)
IGpuBigIntVectorExpression vecExpr = array.AsVectorizedEnumerable()
                                          .SelectMultiply(scalar)
                                          .SelectModulo(modulus);

// Evaluates to a new GpuBigIntArray
GpuBigIntArray resultArray = vecExpr.Evaluate(workspace);

// Supported: Performs a high-throughput multi-pass tree sum over the vector expression
GpuBigIntExpr vectorSumExpr = vecExpr.Sum();
GpuBigInt total = vectorSumExpr.Evaluate();
```

### 3.4 Vectorized Boolean Evaluation and Logical Aggregation
* **Capabilities**: Vectorized comparison methods (`IsGreaterThan`, `IsEqualTo`, etc.) generate `IGpuBoolVectorExpression` without branching. These boolean vectors can then be combined using logical operators (`.And()`, `.Or()`, `.Not()`) and reduced via `.Any()` or `.All()` into a single `GpuBoolExpr`.
* **Constraints**: While `Where` on `IGpuBigIntVectorExpression` performs multi-pass stream compaction using `IGpuBoolVectorExpression` masks, arbitrary C# lambda predicates are reserved for `AsStackEnumerable()`.

```csharp
// Evaluate comparisons across a vector to produce a boolean vector mask
IGpuBoolVectorExpression mask = array.AsVectorizedEnumerable()
                                     .IsGreaterThan(0)
                                     .And(array.AsVectorizedEnumerable().IsLessThan(100));

// Aggregation: Check if any element satisfies the conditions
GpuBoolExpr hasValidElements = mask.Any();
bool result = hasValidElements.Evaluate();
```

### 3.5 Memory Layout Tricks for Vector Reductions (Max / Min)
When performing GPU reductions (such as `.Min()` or `.Max()`) on arrays of signed Big Integers (Sign-Magnitude representation), the GPU's native atomic operations and unsigned comparisons cannot inherently process mixed-sign evaluations accurately.

To achieve branchless, high-throughput comparisons, G# leverages **Offset Binary** memory transformations:
1. **Pre-Processing (Offset Binary)**: Before launching the parallel reduction kernel, the input vector is transformed. Values are converted from Sign-Magnitude to Two's Complement, and the Most Significant Bit (MSB) is flipped (XOR with `0x80000000`). This perfectly aligns signed integers so that `-N < 0 < +N` evaluates correctly using standard unsigned binary comparators on the GPU.
2. **Reduction**: The grid-level Min/Max reduction is performed efficiently using unsigned comparison operations.
3. **Post-Processing (Un-bias)**: Upon retrieving the single scalar result back to the CPU, the MSB is flipped back and the Two's Complement value is decoded to the standard C# `BigInteger` (and subsequently `GpuBigInt` Sign-Magnitude) format.

For `.Sum()` aggregations, the pre-processing step converts the elements strictly to **Two's Complement** (without the Offset Binary MSB flip), allowing unsigned addition kernels to safely compute the sum with overflow/underflow wrap-around, which is then decoded on the CPU.

---

## 4. Execution and Evaluation Strategies

Once an expression tree is constructed, developers choose how and when to dispatch it to the GPU. This choice eliminates CPU-side launch bottlenecks:

| Evaluation Method | .NET Analogy | Recommended Use Case | Syntax Example |
| :--- | :--- | :--- | :--- |
| **`.Evaluate()`** | Standard LINQ `.ToList()` | **One-off execution** of simple or moderately-sized expression trees. | `expr.Evaluate()` |
| **`.CompileToGraph()`**<br>&nbsp;&nbsp;↓<br>**`graph.Execute()`** | `Regex.Compile`<br>`Expression.Compile()` | **Repeated loops** (e.g., training epochs, simulation timesteps). Captures the kernel topology into a CUDA Graph once, reducing host launch overhead to zero. | `using var g = expr.CompileToGraph();`<br>`for (...) { g.Execute(); }` |
| **`.EvaluateWithCudaGraph()`** | Temporary AOT compile | Single execution of **deep, complex expression trees** where sequential kernel launch overhead dominates runtime. | `expr.EvaluateWithCudaGraph()` |

---

## 5. Developer Decision Guide

When writing GPU LINQ queries in G#, follow this decision workflow:

1. **Need a transformed array as output?**
   * Use `AsVectorizedEnumerable()`. It maintains maximum memory bandwidth and outputs a `GpuBigIntArray`.
2. **Need to filter or transform elements into a single scalar?**
   * Start with `AsStackEnumerable()` and define the logic using `Where` and `Select`.
3. **Reducing a large collection to a scalar?**
   * Always chain `.AsParallel()` before `.Sum()`, `.Max()`, etc. to utilize GPU shared-memory reduction.
4. **Calling the expression inside a loop?**
   * Refactor from `.Evaluate()` to `.CompileToGraph()` outside the loop and call `.Execute()` inside the loop to eliminate CPU driver overhead.

---

## 6. Vectorized Division, Modulo, and Warp-Cooperative Arithmetic

The design philosophy of G# GPU LINQ guarantees that "Anything `AsStackEnumerable` can do, `AsVectorizedEnumerable` or `AsParallel` can do, unless physically constrained by hardware limits."

### Vectorized Division and Modulo (`SelectDivide`, `SelectModulo`, `ZipDivide`, `ZipModulo`)
Operations `SelectDivide`, `SelectModulo`, `ZipDivide`, and `ZipModulo` are supported across `AsVectorizedEnumerable()`, `AsStackEnumerable()`, and `AsStackEnumerable().AsParallel()`:
- **Warp-Cooperative Division Kernel**: In `AsVectorizedEnumerable()`, each vector element is assigned to a dedicated warp (`blockDimX = 32`), executing binary long division with warp-level parallel prefix borrow/carry propagation (`WarpScanGp`) and warp-wide comparison ballots (`Warp.AnySync`) without shared memory contention.
- **Warp-Cooperative Montgomery Reduction (`CooperativeMath`)**: For modular multiplication and exponentiation over odd moduli, `CooperativeMath.MontgomeryReduceWarpSync` and `CooperativeMath.MontgomeryMultiplyWarpSync` perform Coarsely Integrated Operand Scanning (CIOS) entirely in warp registers using `Warp.ShuffleSync`, `Warp.ShuffleDownSync`, `Gpu.MultiplyWide`, and `WarpScanGp`.
