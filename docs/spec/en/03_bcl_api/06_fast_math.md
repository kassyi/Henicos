# 6. Fast Math Reference (`Kassyi.GSharp.Core.FastMath`)

The `FastMath` static class provides hardware-native approximation functions for mathematical operations within GPU kernels.

While G# fully supports standard `System.Math` functions (which strictly adhere to IEEE-754 precision requirements), `FastMath` methods bypass these strict requirements, directly mapping to NVIDIA's Special Function Units (SFU) hardware instructions (e.g., `__expf`, `__sinf`).

---

## 6.1 Performance vs Precision Tradeoffs

Using `FastMath` can yield significant throughput improvements (often 2x to 10x faster) at the cost of slight precision degradation and non-standard handling of edge cases (such as NaNs, subnormals, or infinities).

> [!TIP]
> Use `FastMath` for graphics processing, machine learning, and simulations where raw throughput is more critical than exact mathematical precision. Use `System.Math` for scientific computing, financial modeling, or where strict IEEE compliance is required.

---

## 6.2 Supported Methods

All methods strictly operate on single-precision `float` types, as GPUs are heavily optimized for 32-bit floating-point throughput.

### `FastMath.Exp`
Computes the base-e exponential.
```csharp
public static float Exp(float x)
```
* **CUDA Mapping**: `__expf(x)`

### `FastMath.Log`
Computes the natural logarithm.
```csharp
public static float Log(float x)
```
* **CUDA Mapping**: `__logf(x)`

### `FastMath.Sqrt`
Computes the square root.
```csharp
public static float Sqrt(float x)
```
* **CUDA Mapping**: `__fsqrt_rn(x)`

### `FastMath.Abs`
Computes the absolute value.
```csharp
public static float Abs(float x)
```
* **CUDA Mapping**: `fabsf(x)`

---

## 6.3 Example Usage

```csharp
[GpuKernel]
public static void ComputeActivation(float[] weights, float[] outputs)
{
    int i = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
    
    // Using FastMath for a Sigmoid activation function
    // Significantly faster than System.Math.Exp
    float x = weights[i];
    outputs[i] = 1.0f / (1.0f + FastMath.Exp(-x));
}
```
