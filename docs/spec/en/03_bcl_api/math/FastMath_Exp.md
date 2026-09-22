---
title: "FastMath.Exp Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns e raised to the specified power using hardware fast-math."
---

## FastMath.Exp Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns e raised to the specified power using hardware fast-math.

Returns e raised to the specified power using hardware fast-math.

```csharp
public static float Exp(float x);
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
float result = FastMath.Exp(x);
```

## Remarks

Maps to `__expf()` in CUDA PTX for hardware-accelerated approximate calculations.

## See also

- [Math / MathF Exp](Math_Exp.md)
- [Gpu BCL API Overview](../01_api_overview.md)
