---
title: "Math / MathF.Pow Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns a specified number raised to the specified power."
---

## Math / MathF.Pow Method

## Definition

**Namespace:** `System`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns a specified number raised to the specified power.

## Overloads

| Name | Description |
| --- | --- |
| [Pow(float, float)](#pow-float-float) | Returns a specified number raised to the specified power. |
| [Pow(double, double)](#pow-double-double) | Returns a specified number raised to the specified power. |

## Pow(float, float)

Returns a specified number raised to the specified power.

```csharp
public static float Pow(float val1, float val2);
```

#### Parameters

`val1`
`float`
The first single-precision floating-point number.

`val2`
`float`
The second single-precision floating-point number.

#### Returns

`float`
The computed value.

## Pow(double, double)

Returns a specified number raised to the specified power.

```csharp
public static double Pow(double val1, double val2);
```

#### Parameters

`val1`
`double`
The first double-precision floating-point number.

`val2`
`double`
The second double-precision floating-point number.

#### Returns

`double`
The computed value.

## Examples

### Usage

```csharp
// Float precision (MathF)
float f = MathF.Pow(1.0f, 2.0f);
// Double precision (Math)
double d = Math.Pow(1.0, 2.0);
```

## Remarks

Maps natively to `powf()` for float, and `pow()` for double in CUDA C++.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
