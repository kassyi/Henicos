---
title: "Math / MathF.Abs Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the absolute value of a specified number."
---

## Math / MathF.Abs Method

## Definition

**Namespace:** `System`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns the absolute value of a specified number.

## Overloads

| Name | Description |
| --- | --- |
| [Abs(float)](#abs-float) | Returns the absolute value of a specified number. |
| [Abs(double)](#abs-double) | Returns the absolute value of a specified number. |

## Abs(float)

Returns the absolute value of a specified number.

```csharp
[GpuDevice]
public static float Abs(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed value.

## Abs(double)

Returns the absolute value of a specified number.

```csharp
[GpuDevice]
public static double Abs(double x);
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

// MathF.Abs operates on single-precision floating point
float f = MathF.Abs(-5.5f);
Console.WriteLine($"MathF.Abs(-5.5f) = {f:F1}");

// Math.Abs operates on double-precision floating point
double d = Math.Abs(-10.25);
Console.WriteLine($"Math.Abs(-10.25) = {d:F2}");
```
## See also



### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced Math & Lazy Evaluation (Expression Trees)](../../04_guides/07_stdlib_math_and_lazy_evaluation.md)

