---
title: "GpuBigUFloat<TSize> Struct (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Reference for arbitrary-precision unsigned floating-point numbers on the GPU."
---

## GpuBigUFloat<TSize> Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigFloat`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents an arbitrary-precision unsigned floating-point number on the GPU using a fixed-size significand and a separate binary exponent.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigUFloat<TSize> : IEquatable<GpuBigUFloat<TSize>>, IComparable<GpuBigUFloat<TSize>>
    where TSize : unmanaged
```

### Type Parameters

`TSize` _unmanaged_<br>
The unmanaged struct type representing the significand storage size (e.g., `Vector8`).

## Constructors

| Name                                          | Description                                                                |
| --------------------------------------------- | -------------------------------------------------------------------------- |
| `GpuBigUFloat(GpuUIntBigInteger<TSize>, int)` | Initializes a new unsigned float from a significand and a binary exponent. |

## Properties / Fields

| Name          | Description                                                                                          |
| ------------- | ---------------------------------------------------------------------------------------------------- |
| `Significand` | The significand (mantissa) stored as a fixed-size unsigned big integer (`GpuUIntBigInteger<TSize>`). |
| `Exponent`    | The binary exponent of the floating-point number.                                                    |

## Methods

| Name                                                                                                         | Description                                                                         |
| ------------------------------------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------- |
| `AlignRadixPoint(GpuBigUFloat<TSize>, out GpuBigUFloat<TSize>, out GpuBigUFloat<TSize>, out bool, out bool)` | Aligns the radix point between this instance and another unsigned float.            |
| `Normalize(int, bool)`                                                                                       | Normalizes the floating-point significand and adjusts the exponent.                 |
| `Round(GpuBigFloatRoundingMode, bool, bool, bool)`                                                           | Applies IEEE 754 compliant rounding logic.                                          |
| `Add(GpuBigUFloat<TSize>)`                                                                                   | Adds two unsigned floating point values.                                            |
| `Subtract(GpuBigUFloat<TSize>)`                                                                              | Subtracts another unsigned floating point value, clamping to 0 if underflow occurs. |

## Examples

### Unsigned Float Operations

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math, Kassyi.GSharp.Core

```csharp


using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU)
Example.ComputeUFloat();

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void ComputeUFloat()
    {
        // 100 * 2^0 = 100
        var bufA = new LimbBuffer8();
        bufA[0] = 100;
        var sigA = new GpuUIntBigInteger<LimbBuffer8>(bufA);
        var uFloatA = new GpuBigUFloat<LimbBuffer8>(sigA, exponent: 0);

        // 50 * 2^-1 = 25
        var bufB = new LimbBuffer8();
        bufB[0] = 50;
        var sigB = new GpuUIntBigInteger<LimbBuffer8>(bufB);
        var uFloatB = new GpuBigUFloat<LimbBuffer8>(sigB, exponent: -1);

        // Unsigned addition (100 + 25 = 125)
        var sum = uFloatA.Add(uFloatB);
        
        Console.WriteLine($"Computed UFloat Sum: Significand[0]={sum.Significand.Limbs[0]}, Exponent={sum.Exponent}");
    }
}
```

## Remarks

This struct forms the underlying magnitude for [`GpuBigFloat<TSize>`](GpuBigFloat.md). It is separated from the sign bit to eliminate branching overhead in performance-critical GPU kernel algorithms.
It operates purely as an unmanaged structure, avoiding heap allocations through C# 12 `[InlineArray]`. For details, see [Memory Management and Zero-Allocation](../00_index.md#memory-management-and-zero-allocation).

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigFloat<TSize> Struct (G#)](GpuBigFloat.md)
- [GpuBigFloatRoundingMode Enum (G#)](GpuBigFloatRoundingMode.md)
