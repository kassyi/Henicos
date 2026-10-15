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

```csharp
public static float Log(float x);
```

#### Parameters

`x` _float_<br>
The single-precision floating-point number whose logarithm is to be computed.

#### Returns

_float_<br>
The natural logarithm of `x` calculated using fast hardware approximation on GPU or `MathF.Log(x)` on CPU fallback.

## Examples

### Computing Fast Natural Logarithm

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
float f = FastMath.Log(10.0f);
Console.WriteLine($"FastMath.Log(10.0f) = {f:F4}");
```

## Remarks

Usable in both CPU fallback (delegating to `MathF.Log`) and GPU device kernels (mapped to the CUDA `__logf` fast-math intrinsic).

## See also

### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Log Method](Math_Log.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
