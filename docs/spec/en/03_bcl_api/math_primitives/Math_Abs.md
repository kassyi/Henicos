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
| [Abs(int)](#abs-int) | Returns the absolute value of a 32-bit signed integer. |

## Abs(float)

Returns the absolute value of a specified number.

```csharp
public static float Abs(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed value.

#### Exceptions

None. Does not throw exceptions; returns `float.NaN` if `x` is `float.NaN`.

## Abs(double)

Returns the absolute value of a specified number.

```csharp
public static double Abs(double x);
```

#### Parameters

`x` <i>double</i><br>
A double-precision floating-point number.

#### Returns

<i>double</i><br>
The computed value.

#### Exceptions

None. Does not throw exceptions; returns `double.NaN` if `x` is `double.NaN`.

<a id="abs-int"></a>

## Abs(int)

Returns the absolute value of a 32-bit signed integer.

```csharp
public static int Abs(int value);
```

#### Parameters

`value` <i>int</i><br>
A 32-bit signed integer.

#### Returns

<i>int</i><br>
The absolute value of `value`.

#### Exceptions

`OverflowException`<br>
`value` is `int.MinValue`. The managed path throws it as C# does. The native CPU build and the checked CUDA build are to throw it with the source location, and the release CUDA build does not check ([6. Semantics](../../01_language/06_semantics.md)); today the native CPU build and CUDA return `int.MinValue` instead (#947).

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

