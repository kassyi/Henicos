---
title: "Math / MathF.Exp Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns e raised to the specified power."
---

## Math / MathF.Exp Method

## Definition

**Namespace:** `System`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns e raised to the specified power.

## Overloads

| Name | Description |
| --- | --- |
| [Exp(float)](#exp-float) | Returns e raised to the specified power. |
| [Exp(double)](#exp-double) | Returns e raised to the specified power. |

## Exp(float)

Returns e raised to the specified power.

```csharp
[GpuDevice]
public static float Exp(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed value.

## Exp(double)

Returns e raised to the specified power.

```csharp
[GpuDevice]
public static double Exp(double x);
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

// MathF.Exp operates on single-precision floating point
float f = MathF.Exp(1.0f);
Console.WriteLine($"MathF.Exp(1.0f) = {f:F3}");

// Math.Exp operates on double-precision floating point
double d = Math.Exp(2.0);
Console.WriteLine($"Math.Exp(2.0) = {d:F3}");
```
## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [FastMath.Exp Method](FastMath_Exp.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced Math & Lazy Evaluation (Expression Trees)](../../04_guides/07_stdlib_math_and_lazy_evaluation.md)

