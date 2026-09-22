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
[GpuKernel]
public static float Exp(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed fast-math value.

## Examples

### Fast Math Approximation

```csharp
// Execution Context: G# Kernel (GPU)
float result = FastMath.Exp(x);
```

## Remarks

Maps natively to `expf()` or `__expf()` for float in CUDA C++.

## See also

- [Math / MathF Exp](Math_Exp.md)
- [Gpu BCL API Overview](../01_api_overview.md)
