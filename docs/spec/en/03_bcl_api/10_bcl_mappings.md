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
| `TrailingZeroCount(value)`| `uint`, `ulong`, `nuint` | (Emulated via `__clz`) | Counts the number of trailing zero bits. |
| `Log2(value)` | `uint`, `ulong`, `nuint` | (Emulated via `__clz`) | Returns the integer base-2 logarithm of the specified value. |
| `RotateLeft(value, offset)`| `uint`, `ulong`, `nuint` | (Emulated PTX macro) | Rotates the bits of the integer left. |
| `RotateRight(value, offset)`| `uint`, `ulong`, `nuint` | (Emulated PTX macro) | Rotates the bits of the integer right. |

#### Example
```csharp
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
> Floating-point atomics (`float`) are not supported by the standard `System.Threading.Interlocked` class in .NET. To perform atomic additions on floats, you must use the `Gpu.AtomicAdd(ref float, float)` intrinsic instead.

---

## 10.3 `System.Math` and `System.MathF`

A wide variety of standard mathematical operations are mapped to the CUDA `math_functions.h` standard library. 
*(Note: These functions adhere to IEEE-754 precision. For raw, uncompromised speed at the cost of slight precision loss, use `Kassyi.GSharp.Core.FastMath` instead).*

| .NET Method | CUDA Mapping (MathF - Single Float) | CUDA Mapping (Math - Double) |
| :--- | :--- | :--- |
| `Math.Abs` / `MathF.Abs` | `fabsf` | `fabs` |
| `Math.Min` / `MathF.Min` | `fminf` | `fmin` |
| `Math.Max` / `MathF.Max` | `fmaxf` | `fmax` |
| `Math.Sqrt` / `MathF.Sqrt` | `sqrtf` | `sqrt` |
| `Math.Exp` / `MathF.Exp` | `expf` | `exp` |
| `Math.Log` / `MathF.Log` | `logf` | `log` |
| `Math.Sin` / `MathF.Sin` | `sinf` | `sin` |
| `Math.Cos` / `MathF.Cos` | `cosf` | `cos` |
| `Math.Tan` / `MathF.Tan` | `tanf` | `tan` |
| `Math.Round` / `MathF.Round` | `roundf` | `round` |
| `Math.Ceiling` / `MathF.Ceiling` | `ceilf` | `ceil` |
| `Math.Floor` / `MathF.Floor` | `floorf` | `floor` |
| `Math.Pow` / `MathF.Pow` | `powf` | `pow` |

> [!WARNING]
> While `System.Math` returns `double` by default in C#, GPU execution favors `float` (single precision) due to massive hardware architectural advantages. Ensure you are explicitly using `System.MathF.*` equivalents where appropriate to avoid unexpected promotion to double-precision performance penalties.
