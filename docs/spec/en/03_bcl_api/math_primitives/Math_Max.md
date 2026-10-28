---
title: "Math / MathF.Max Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the larger of two specified numbers."
---

## Math / MathF.Max Method

## Definition

**Namespace:** `System`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns the larger of two specified numbers.

## Overloads

| Name | Description |
| --- | --- |
| [Max(float, float)](#max-float-float) | Returns the larger of two specified numbers. |
| [Max(double, double)](#max-double-double) | Returns the larger of two specified numbers. |

## Max(float, float)

Returns the larger of two specified numbers.

```csharp
[GpuDevice]
public static float Max(float val1, float val2);
```

#### Parameters

`val1` <i>float</i><br>
The first single-precision floating-point number.

`val2` <i>float</i><br>
The second single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed value.

#### Exceptions

None. Does not throw exceptions.

## Max(double, double)

Returns the larger of two specified numbers.

```csharp
[GpuDevice]
public static double Max(double val1, double val2);
```

#### Parameters

`val1` <i>double</i><br>
The first double-precision floating-point number.

`val2` <i>double</i><br>
The second double-precision floating-point number.

#### Returns

<i>double</i><br>
The computed value.

#### Exceptions

None. Does not throw exceptions.

## Remarks

The result is the .NET result on the GPU, the native CPU build and the managed path (IEEE 754:2019 `maximum`): when either input is NaN, the result is NaN, and `+0.0` is larger than `-0.0`, so `Math.Max(-0.0, 0.0)` is `+0.0`. `float.Max` and `double.Max` are the same operation. Compare a NaN result with `float.IsNaN` / `double.IsNaN`: its payload can differ between the GPU and .NET.

On compute capability 8.0 and newer, the `float` overload is one instruction (PTX `max.NaN.f32`). The `double` overload adds a NaN test. When NaN inputs need no propagation, `float.MaxNumber` / `double.MaxNumber` ignore a NaN input (the result is NaN only when both are NaN), order the signed zeros in the same way, and are one instruction for both types.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;

// MathF.Max operates on single-precision floating point
float f = MathF.Max(10.5f, 20.1f);
Console.WriteLine($"MathF.Max(10.5f, 20.1f) = {f:F1}");

// Math.Max operates on double-precision floating point
double d = Math.Max(5.0, -5.0);
Console.WriteLine($"Math.Max(5.0, -5.0) = {d:F1}");
```
## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Vector2, Vector3, Vector4 Structs (System.Numerics)](VectorTypes.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced Math & Lazy Evaluation (Expression Trees)](../../04_guides/07_stdlib_math_and_lazy_evaluation.md)

