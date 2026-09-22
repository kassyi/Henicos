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
[GpuKernel]
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
[GpuKernel]
public static double Exp(double x);
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
float f = MathF.Exp(1.0f);
// Double precision (Math)
double d = Math.Exp(1.0);
```

## Remarks

Maps natively to `expf()` for float, and `exp()` for double in CUDA C++.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
