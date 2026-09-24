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
[GpuDevice]
public static float Abs(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed fast-math absolute value.

## Examples

### Fast Math Approximation

> [!NOTE] Requires assemblies: Kassyi.GSharp.Core

```csharp
// Execution Context: G# Kernel (GPU)
public class Example
{
    [GpuDeviceFunction]
    public static void Kernel(float x)
    {
        float result = FastMath.Abs(x);
    }
}
```

## Remarks

Maps natively to `fabsf()` or `__fbsf()` for float in CUDA C++.

## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Abs Method](Math_Abs.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
