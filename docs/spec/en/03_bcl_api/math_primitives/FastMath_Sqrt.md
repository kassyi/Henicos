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

```csharp
public static float Sqrt(float x);
```

#### Parameters

`x` _float_<br>
The single-precision floating-point number whose square root is to be computed.

#### Returns

_float_<br>
The square root of `x` calculated using fast hardware approximation on GPU or `MathF.Sqrt(x)` on CPU fallback.

## Examples

### Computing Fast Square Root

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
float f = FastMath.Sqrt(16.0f);
Console.WriteLine($"FastMath.Sqrt(16.0f) = {f:F1}");
```

## Remarks

On the GPU it maps to the CUDA intrinsic `__fsqrt_rn` (square root rounded to nearest). The native CPU build maps it to `sqrtf`, and the managed path calls `MathF.Sqrt`.
## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Sqrt Method](Math_Sqrt.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)

