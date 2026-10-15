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

```csharp
public static float Exp(float x);
```

#### Parameters

`x` _float_<br>
The single-precision floating-point exponent.

#### Returns

_float_<br>
The value `e^x` calculated using fast hardware approximation on GPU or `MathF.Exp(x)` on CPU fallback.

## Examples

### Computing Fast Exponential

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
float f = FastMath.Exp(1.0f);
Console.WriteLine($"FastMath.Exp(1.0f) = {f:F4}");
```

## Remarks

Usable in both CPU fallback (delegating to `MathF.Exp`) and GPU device kernels (mapped to the CUDA `__expf` fast-math intrinsic).

## See also

### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Exp Method](Math_Exp.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
