---
title: "FastMath.Sqrt Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the square root using hardware fast-math."
---

## FastMath.Sqrt Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the square root using hardware fast-math.

Returns the square root using hardware fast-math.

```csharp
public static float Sqrt(float x);
```

#### Parameters

`x`
`float`
A single-precision floating-point number.

#### Returns

`float`
The computed fast-math value.

## Examples

### Fast Math Approximation

```csharp
// Execution Context: G# Kernel
float result = FastMath.Sqrt(x);
```

## Remarks

Maps to `__sqrtf()` in CUDA PTX for hardware-accelerated approximate calculations.

## See also

- [Math / MathF Sqrt](Math_Sqrt.md)
- [Gpu BCL API Overview](../01_api_overview.md)
