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
[GpuKernel]
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
[GpuKernel]
public static double Abs(double x);
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
float f = MathF.Abs(1.0f);
// Double precision (Math)
double d = Math.Abs(1.0);
```

## Remarks

Maps natively to `fabsf()` for float, `fabs()` for double, and `abs()` for int in CUDA C++.

## See also

- [Gpu BCL API Overview](../00_overview.md)
