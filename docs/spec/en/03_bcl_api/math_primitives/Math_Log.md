---
title: "Math / MathF.Log Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the natural (base e) logarithm of a specified number."
---

## Math / MathF.Log Method

## Definition

**Namespace:** `System`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns the natural (base e) logarithm of a specified number.

## Overloads

| Name | Description |
| --- | --- |
| [Log(float)](#log-float) | Returns the natural (base e) logarithm of a specified number. |
| [Log(double)](#log-double) | Returns the natural (base e) logarithm of a specified number. |

## Log(float)

Returns the natural (base e) logarithm of a specified number.

```csharp
[GpuKernel]
public static float Log(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed value.

## Log(double)

Returns the natural (base e) logarithm of a specified number.

```csharp
[GpuKernel]
public static double Log(double x);
```

#### Parameters

`x` <i>double</i><br>
A double-precision floating-point number.

#### Returns

<i>double</i><br>
The computed value.

## Examples

### Usage

```csharp
// Execution Context: G# Kernel (GPU)
// Float precision (MathF)
float f = MathF.Log(1.0f);
// Double precision (Math)
double d = Math.Log(1.0);
```

## Remarks

Maps natively to `logf()` for float, and `log()` for double in CUDA C++.

## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [FastMath.Log Method](FastMath_Log.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced Math & Lazy Evaluation (Expression Trees)](../../04_guides/07_stdlib_math_and_lazy_evaluation.md)

