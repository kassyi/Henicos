---
title: "FastMath.Abs Method"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Returns the absolute value of a specified number using hardware fast-math."
---

## FastMath.Abs Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the absolute value of a specified number using hardware fast-math.

```csharp
public static float Abs(float x);
```

#### Parameters

`x` _float_<br>
The single-precision floating-point value.

#### Returns

_float_<br>
The absolute value of `x`.

## Examples

### Computing Fast Absolute Value

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
float f = FastMath.Abs(-5.5f);
Console.WriteLine($"FastMath.Abs(-5.5f) = {f:F1}");
```

## Remarks

Usable in both CPU fallback (delegating to `MathF.Abs`) and GPU device kernels (mapped to the CUDA `fabsf` intrinsic).

## See also

### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Abs Method](Math_Abs.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
