---
title: "Math / MathF.Sin Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the sine of the specified angle."
---

## Math / MathF.Sin Method

## Definition

**Namespace:** `System`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns the sine of the specified angle.

## Overloads

| Name | Description |
| --- | --- |
| [Sin(float)](#sin-float) | Returns the sine of the specified angle. |
| [Sin(double)](#sin-double) | Returns the sine of the specified angle. |

## Sin(float)

Returns the sine of the specified angle.

```csharp
[GpuDevice]
public static float Sin(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed value.

## Sin(double)

Returns the sine of the specified angle.

```csharp
[GpuDevice]
public static double Sin(double x);
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

// MathF.Sin operates on single-precision floating point
float f = MathF.Sin(MathF.PI / 2);
Console.WriteLine($"MathF.Sin(PI/2) = {f:F1}");

// Math.Sin operates on double-precision floating point
double d = Math.Sin(Math.PI);
Console.WriteLine($"Math.Sin(PI) = {d:F1}");
```
## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [FastMath.Exp Method](FastMath_Exp.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced Math & Lazy Evaluation (Expression Trees)](../../04_guides/07_stdlib_math_and_lazy_evaluation.md)

