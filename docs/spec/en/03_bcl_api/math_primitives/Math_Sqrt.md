---
title: "Math / MathF.Sqrt Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the square root of a specified number."
---

## Math / MathF.Sqrt Method

## Definition

**Namespace:** `System`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns the square root of a specified number.

## Overloads

| Name | Description |
| --- | --- |
| [Sqrt(float)](#sqrt-float) | Returns the square root of a specified number. |
| [Sqrt(double)](#sqrt-double) | Returns the square root of a specified number. |

## Sqrt(float)

Returns the square root of a specified number.

```csharp
[GpuDevice]
public static float Sqrt(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed value.

## Sqrt(double)

Returns the square root of a specified number.

```csharp
[GpuDevice]
public static double Sqrt(double x);
```

#### Parameters

`x` <i>double</i><br>
A double-precision floating-point number.

#### Returns

<i>double</i><br>
The computed value.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;

// MathF.Sqrt operates on single-precision floating point
float f = MathF.Sqrt(16.0f);
Console.WriteLine($"MathF.Sqrt(16.0f) = {f:F1}");

// Math.Sqrt operates on double-precision floating point
double d = Math.Sqrt(25.0);
Console.WriteLine($"Math.Sqrt(25.0) = {d:F1}");
```
## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [FastMath.Sqrt Method](FastMath_Sqrt.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced Math & Lazy Evaluation (Expression Trees)](../../04_guides/07_stdlib_math_and_lazy_evaluation.md)

