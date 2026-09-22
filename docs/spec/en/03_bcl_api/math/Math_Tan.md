---
title: "Math / MathF.Tan Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the tangent of the specified angle."
---

## Math / MathF.Tan Method

## Definition

**Namespace:** `System`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns the tangent of the specified angle.

## Overloads

| Name | Description |
| --- | --- |
| [Tan(float)](#tan-float) | Returns the tangent of the specified angle. |
| [Tan(double)](#tan-double) | Returns the tangent of the specified angle. |

## Tan(float)

Returns the tangent of the specified angle.

```csharp
public static float Tan(float x);
```

#### Parameters

`x`
`float`
A single-precision floating-point number.

#### Returns

`float`
The computed value.

## Tan(double)

Returns the tangent of the specified angle.

```csharp
public static double Tan(double x);
```

#### Parameters

`x`
`double`
A double-precision floating-point number.

#### Returns

`double`
The computed value.

## Examples

### Usage

```csharp
// Float precision (MathF)
float f = MathF.Tan(1.0f);
// Double precision (Math)
double d = Math.Tan(1.0);
```

## Remarks

Maps natively to `tanf()` for float, and `tan()` for double in CUDA C++.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
