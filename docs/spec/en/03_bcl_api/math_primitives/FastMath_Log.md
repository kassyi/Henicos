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
[GpuDevice]
public static float Log(float x);
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
float result = FastMath.Log(x);
```

## Remarks

Maps to `__logf()` in CUDA PTX for hardware-accelerated approximate calculations.

## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Log Method](Math_Log.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)

