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
public static float Sin(float x);
```

#### Parameters

`x`
`float`
A single-precision floating-point number.

#### Returns

`float`
The computed value.

## Sin(double)

Returns the sine of the specified angle.

```csharp
public static double Sin(double x);
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
float f = MathF.Sin(1.0f);
// Double precision (Math)
double d = Math.Sin(1.0);
```

## Remarks

Maps natively to `sinf()` for float, and `sin()` for double in CUDA C++.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
