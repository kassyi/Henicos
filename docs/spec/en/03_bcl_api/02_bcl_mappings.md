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

| .NET Method | Supported Types | CUDA/PTX Mapping | Description |
| :--- | :--- | :--- | :--- |
| `PopCount(value)` | `uint`, `ulong`, `nuint` | `__popc`, `__popcll` | Counts the number of set bits (1s) in the integer. |
| `LeadingZeroCount(value)` | `uint`, `ulong`, `nuint` | `__clz`, `__clzll` | Counts the number of leading zero bits. |
| `TrailingZeroCount(value)` | `uint`, `ulong`, `nuint` | (Emulated via `__clz`) | Counts the number of trailing zero bits. |
| `Log2(value)` | `uint`, `ulong`, `nuint` | (Emulated via `__clz`) | Returns the integer base-2 logarithm of the specified value. |
| `RotateLeft(value, offset)` | `uint`, `ulong`, `nuint` | (Emulated PTX macro) | Rotates the bits of the integer left. |
| `RotateRight(value, offset)` | `uint`, `ulong`, `nuint` | (Emulated PTX macro) | Rotates the bits of the integer right. |

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

| .NET Method | Supported Types | CUDA/PTX Mapping |
| :--- | :--- | :--- |
| `Add(ref location, value)` | `int`, `long` | `atomicAdd` |
| `Exchange(ref location, value)` | `int`, `uint`, `long`, `ulong` | `atomicExch` |
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

| .NET Method | CUDA Mapping (MathF - Single Float) | CUDA Mapping (Math - Double) |
| :--- | :--- | :--- |
| `Math.Abs` / `MathF.Abs` | `fabsf` | `fabs` |
| `Math.Min` / `MathF.Min` / `float.Min` / `double.Min` | `__gsharp_min_f32` | `__gsharp_min_f64` |
| `Math.Max` / `MathF.Max` / `float.Max` / `double.Max` | `__gsharp_max_f32` | `__gsharp_max_f64` |
| `float.MinNumber` / `double.MinNumber` | `__gsharp_min_number_f32` (`fminf`) | `__gsharp_min_number_f64` (`fmin`) |
| `float.MaxNumber` / `double.MaxNumber` | `__gsharp_max_number_f32` (`fmaxf`) | `__gsharp_max_number_f64` (`fmax`) |
| `Math.Sqrt` / `MathF.Sqrt` | `sqrtf` | `sqrt` |
| `Math.Exp` / `MathF.Exp` | `expf` | `exp` |
| `Math.Log` / `MathF.Log` | `logf` | `log` |
| `Math.Sin` / `MathF.Sin` | `sinf` | `sin` |
| `Math.Cos` / `MathF.Cos` | `cosf` | `cos` |
| `Math.Tan` / `MathF.Tan` | `tanf` | `tan` |
| `Math.Round(double)` / `MathF.Round(float)` | `rintf` | `rint` |
| `Math.Ceiling` / `MathF.Ceiling` | `ceilf` | `ceil` |
| `Math.Floor` / `MathF.Floor` | `floorf` | `floor` |
| `Math.Pow` / `MathF.Pow` | `powf` | `pow` |

`Math.Max` / `Math.Min` (and `MathF`, `float.Max`, `double.Max`) give the .NET result on every path: NaN when either input is NaN, and `+0.0` is larger than `-0.0` (IEEE 754:2019 `maximum` / `minimum`). The C `fmaxf` / `fmax` return the other input for a NaN, so they are not used for these. On compute capability 8.0 and newer, the `float` overloads are one instruction (PTX `max.NaN.f32`), the cost of `fmaxf`; the `double` overloads add a NaN test. `float.MaxNumber` / `MinNumber` and `double.MaxNumber` / `MinNumber` (IEEE `maximumNumber` / `minimumNumber`: a NaN input is ignored, `+0.0` is larger than `-0.0`) compile to `fmaxf` / `fmax`, which are one instruction for both types; use them when NaN inputs need no propagation. `Math.Clamp` compares the value with `min` first, as .NET does, so `Math.Clamp(NaN, 0, 1)` is NaN and the sign of a zero value is kept. The NaN that a CUDA result holds can have a different payload from the .NET one; compare with `IsNaN`.

`Math.Round` and `MathF.Round` round half to even, like the C# default (`Math.Round(2.5)` is `2`). They map to `rint` / `rintf`, not to `round`, which rounds half away from zero. Only the single-argument `double` and `float` overloads are supported. The overloads with a digits argument or a `MidpointRounding` argument, and `decimal`, are reported as `GSH0003`.

`Math.Abs`, `Math.Min` and `Math.Max` also have an `int` overload (`abs`, `min`, `max`), and `Math.Clamp` (`float`, `double`, `int`) maps to `__gsharp_clamp`. The constants (`Math.PI`, `Math.E`, `MathF.PI`) are written as their values. The C++ dialect maps the same members as the CUDA dialect. Any other member or overload (for example `Math.Abs(long)` or `MathF.Cbrt`) is reported as `GSH0003`, with its parameter types and the dialect that does not map it.

---

## 10.4 `System.MemoryExtensions.Sort`

`Sort` sorts the span (and the items) in place, in the thread that calls it. It is not a cooperative sort: when every thread of a launch calls it on the same span, every thread sorts that span at the same time, which is a data race. To sort a shared span once, call it from one thread, for example under `if ((ThreadIndex.X | ThreadIndex.Y | ThreadIndex.Z | BlockIndex.X | BlockIndex.Y | BlockIndex.Z) == 0)`.

The sort is a port of the .NET introsort (`ArraySortHelper`; the algorithm is the one of dotnet/runtime commit `e750854de9`, MIT License, see `THIRD-PARTY-NOTICES.txt`), shared by the CUDA and CPU dialects. A comparer struct is copied once and that one copy is used for the whole sort, as .NET 10 does (it boxes the comparer once). It allocates nothing, and every path gives the order of .NET, including the order of equal keys and of their items (the sort is not stable, but it is the same unstable order). For `float` and `double`, NaN keys come first and `-0.0` equals `0.0`, as in .NET.

| Overload | Supported form |
| :--- | :--- |
| `Sort(span)` | Elements that are a number other than `nint` and `nuint`, `char`, `bool` or an enum (ordered by value), or a user struct that implements `IComparable<T>` in the same compilation |
| `Sort(span, comparer)` | A non-generic user struct that implements one `IComparer<T>` in the same compilation; `null` and `Comparer<T>.Default` sort by the default order |
| `Sort(span, comparison)` | A lambda or a `[GpuDeviceFunction]` method group |
| `Sort(keys, items)` | As `Sort(span)`; each item moves with its key |
| `Sort(keys, items, comparer)` | As `Sort(span, comparer)` |
| `Sort(keys, items, comparison)` | As `Sort(span, comparison)` |

Everything else is reported as `GSH0003` at the argument that causes it: a comparer that is a class, an interface or a generic struct, a comparer struct that implements `IComparer<T>` for more than one `T`, a `Comparison<T>` variable or `null`, elements without a default order (`nint` and `nuint` keys included), and named arguments in another order than the parameters.

The span, the items and the comparer are evaluated once each, in the C# order. As in .NET, keys and items of different lengths throw `ArgumentException`, and so does a comparer whose results are inconsistent when the partition scan runs past the span. A failure inside the comparison is wrapped as .NET wraps it: an index out of range becomes `ArgumentException`, anything else `InvalidOperationException`. On the CPU paths these are the .NET exceptions; on CUDA the kernel stops with a trap.

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

