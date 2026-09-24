---
title: "GpuBigFloatRoundingMode Enum (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Reference for IEEE 754 compliant rounding modes in arbitrary-precision GPU floating-point operations."
---

## GpuBigFloatRoundingMode Enum

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigFloat`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Specifies the IEEE 754 compliant rounding mode for arbitrary-precision GPU floating-point operations.

```csharp
public enum GpuBigFloatRoundingMode
```

## Members

| Member Name                   | Value | Description                                                                                      |
| ----------------------------- | ----- | ------------------------------------------------------------------------------------------------ |
| `RoundToNearestEven`          | `0`   | Round to nearest, with ties rounding to the nearest even integer significand (IEEE 754 default). |
| `RoundTowardZero`             | `1`   | Round toward zero (truncation).                                                                  |
| `RoundTowardPositiveInfinity` | `2`   | Round toward positive infinity.                                                                  |
| `RoundTowardNegativeInfinity` | `3`   | Round toward negative infinity.                                                                  |

## Examples

### Specifying a Rounding Mode

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math, Kassyi.GSharp.Core

```csharp


using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU)
Example.RoundFloat(new GpuBigUFloat<LimbBuffer8>(default, 0));

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void RoundFloat(GpuBigUFloat<LimbBuffer8> ufloat)
    {
        // Apply rounding towards zero (truncation)
        var rounded = ufloat.Round(
            GpuBigFloatRoundingMode.RoundTowardZero,
            guardBit: true,
            roundBit: false,
            stickyBit: true
        );
        
        Console.WriteLine($"Rounded Exponent: {rounded.Exponent}");
    }
}
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigFloat<TSize> Struct (G#)](GpuBigFloat.md)
- [GpuBigUFloat<TSize> Struct (G#)](GpuBigUFloat.md)
