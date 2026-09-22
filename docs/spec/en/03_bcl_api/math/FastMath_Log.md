---
title: "FastMath.Log Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the natural (base e) logarithm using hardware fast-math."
---

## FastMath.Log Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the natural (base e) logarithm using hardware fast-math.

Returns the natural (base e) logarithm using hardware fast-math.

```csharp
public static float Log(float x);
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
float result = FastMath.Log(x);
```

## Remarks

Maps to `__logf()` in CUDA PTX for hardware-accelerated approximate calculations.

## See also

- [Math / MathF Log](Math_Log.md)
- [Gpu BCL API Overview](../01_api_overview.md)
