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
[GpuKernel]
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

## Max(double, double)

Returns the larger of two specified numbers.

```csharp
[GpuKernel]
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

## Examples

### Usage

```csharp
// Execution Context: G# Kernel (GPU)
// Float precision (MathF)
float f = MathF.Max(1.0f, 2.0f);
// Double precision (Math)
double d = Math.Max(1.0, 2.0);
```

## Remarks

Maps natively to `fmaxf()` for float, `fmax()` for double, and `max()` for int in CUDA C++.

## See also

- [Gpu BCL API Overview](../00_overview.md)
