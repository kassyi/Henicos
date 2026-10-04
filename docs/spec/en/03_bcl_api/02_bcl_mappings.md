---
title: "10. Standard BCL Mappings"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "The G# Transpiler maps a specific subset of the standard .NET Base Class Library (BCL) directly to optimized NVIDIA CUDA intrinsic hardware instruc..."
---

# 10. Standard BCL Mappings

The G# Transpiler maps a specific subset of the standard .NET Base Class Library (BCL) directly to optimized NVIDIA CUDA intrinsic hardware instructions.

This allows developers to write mathematically intensive kernels using standard .NET syntax (`System.Math`, `System.Numerics`) while guaranteeing zero-overhead native GPU execution.

---

## 10.1 `System.Numerics.BitOperations`

These methods are mapped directly to PTX (Parallel Thread Execution) instructions, ensuring single-cycle or near-single-cycle execution throughput.

| .NET Method                  | Supported Types          | CUDA/PTX Mapping       | Description                                                  |
| :--------------------------- | :----------------------- | :--------------------- | :----------------------------------------------------------- |
| `PopCount(value)`            | `uint`, `ulong`, `nuint` | `__popc`, `__popcll`   | Counts the number of set bits (1s) in the integer.           |
| `LeadingZeroCount(value)`    | `uint`, `ulong`, `nuint` | `__clz`, `__clzll`     | Counts the number of leading zero bits.                      |
| `TrailingZeroCount(value)`   | `uint`, `ulong`, `nuint` | (Emulated via `__clz`) | Counts the number of trailing zero bits.                     |
| `Log2(value)`                | `uint`, `ulong`, `nuint` | (Emulated via `__clz`) | Returns the integer base-2 logarithm of the specified value. |
| `RotateLeft(value, offset)`  | `uint`, `ulong`, `nuint` | (Emulated PTX macro)   | Rotates the bits of the integer left.                        |
| `RotateRight(value, offset)` | `uint`, `ulong`, `nuint` | (Emulated PTX macro)   | Rotates the bits of the integer right.                       |

#### Example

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void CalculatePopCount(uint[] data, int[] results)
{
    int tid = ThreadIndex.X + BlockIndex.X * BlockDimension.X;

    // Maps directly to the __popc hardware instruction
    results[tid] = System.Numerics.BitOperations.PopCount(data[tid]);
}
```

---

## 10.2 `System.Threading.Interlocked`

Standard .NET Interlocked operations are safely mapped to CUDA atomic instructions, allowing threads to perform synchronized memory operations without locks.

| .NET Method                                       | Supported Types                | CUDA/PTX Mapping               |
| :------------------------------------------------ | :----------------------------- | :----------------------------- |
| `Add(ref location, value)`                        | `int`, `long`                  | `atomicAdd`                    |
| `Exchange(ref location, value)`                   | `int`, `uint`, `long`, `ulong` | `atomicExch`                   |
| `CompareExchange(ref location, value, comparand)` | `int`, `uint`, `long`, `ulong` | `atomicCAS` (Compare And Swap) |

> [!NOTE]
> **Interlocked vs Gpu.AtomicAdd**
> 
> G# supports both `System.Threading.Interlocked` and `Gpu.AtomicAdd`. How should you choose?
> - **Use `Interlocked` (Recommended):** For standard integer atomics (e.g., `Interlocked.Add(ref myInt, 1)`). This is the C# standard, making your code highly portable and compatible with CPU fallback modes.
> - **Use `Gpu.AtomicAdd`:** When you need atomic operations on `float` or `double`. The standard C# `.NET` BCL does not support floating-point atomic additions natively via `Interlocked`, but GPU hardware does. Use `Gpu.AtomicAdd` to unlock hardware-accelerated floating-point atomics.

---

## 10.3 `System.Math` and `System.MathF`

> [!NOTE]
> **Performance and Precision Considerations:**
> When using standard Math functions, be cautious of unintended double-precision promotion. For a detailed breakdown of precision vs. performance on GPU architectures, and why `float` is heavily favored over `double`, see the **[Precision and Performance Guide](../04_guides/05_precision_and_performance_guide.md)**.

A wide variety of standard mathematical operations are mapped to the CUDA `math_functions.h` standard library.
_(Note: These functions adhere to IEEE-754 precision. For raw, uncompromised speed at the cost of slight precision loss, use `Kassyi.GSharp.Core.FastMath` instead)._

| .NET Method                      | CUDA Mapping (MathF - Single Float) | CUDA Mapping (Math - Double) |
| :------------------------------- | :---------------------------------- | :--------------------------- |
| `Math.Abs` / `MathF.Abs`         | `fabsf`                             | `fabs`                       |
| `Math.Min` / `MathF.Min`         | `fminf`                             | `fmin`                       |
| `Math.Max` / `MathF.Max`         | `fmaxf`                             | `fmax`                       |
| `Math.Sqrt` / `MathF.Sqrt`       | `sqrtf`                             | `sqrt`                       |
| `Math.Exp` / `MathF.Exp`         | `expf`                              | `exp`                        |
| `Math.Log` / `MathF.Log`         | `logf`                              | `log`                        |
| `Math.Sin` / `MathF.Sin`         | `sinf`                              | `sin`                        |
| `Math.Cos` / `MathF.Cos`         | `cosf`                              | `cos`                        |
| `Math.Tan` / `MathF.Tan`         | `tanf`                              | `tan`                        |
| `Math.Round` / `MathF.Round`     | `roundf`                            | `round`                      |
| `Math.Ceiling` / `MathF.Ceiling` | `ceilf`                             | `ceil`                       |
| `Math.Floor` / `MathF.Floor`     | `floorf`                            | `floor`                      |
| `Math.Pow` / `MathF.Pow`         | `powf`                              | `pow`                        |

## See also



### Architecture

- [2. Compiler and Runtime Architecture](../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../02_architecture/01_aot_transpiler.md)

### BCL API > Attributes

- [Attributes](attributes/00_index.md)
- [Kassyi.GSharp.Core: Advanced Mapping Attributes](attributes/AdvancedMappingAttributes.md)

### Guides

- [4. Practical Guides and Best Practices](../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../04_guides/08_advanced_custom_math_kernels.md)

