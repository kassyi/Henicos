---
title: "GpuBigFloat<TSize> Struct (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Reference for arbitrary-precision floating-point numbers on the GPU."
---

## GpuBigFloat<TSize> Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigFloat`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents an arbitrary-precision signed floating-point number on the GPU.
It operates entirely on the GPU and is composed of an underlying unsigned magnitude ([`GpuBigUFloat<TSize>`](GpuBigUFloat.md)) and a sign bit.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct GpuBigFloat<TSize> : IEquatable<GpuBigFloat<TSize>>, IComparable<GpuBigFloat<TSize>>
    where TSize : unmanaged
```

### Type Parameters

`TSize` _unmanaged_<br>
The unmanaged struct type representing the significand storage size (e.g., `Vector8`).

## Constructors

| Name                                               | Description                                                                         |
| -------------------------------------------------- | ----------------------------------------------------------------------------------- |
| `GpuBigFloat(GpuBigUFloat<TSize>, bool)`           | Initializes a new floating point wrapper around an unsigned float.                  |
| `GpuBigFloat(GpuUIntBigInteger<TSize>, int, bool)` | Initializes a new floating point wrapper from significand, exponent, and sign flag. |

## Properties / Fields

| Name         | Description                                                                   |
| ------------ | ----------------------------------------------------------------------------- |
| `UFloat`     | The underlying unsigned floating-point value payload (`GpuBigUFloat<TSize>`). |
| `IsNegative` | `true` if the floating-point number is strictly negative.                     |

## Methods

| Name                                                                                                      | Description                                                                     |
| --------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| `AlignRadixPoint(GpuBigFloat<TSize>, out GpuBigFloat<TSize>, out GpuBigFloat<TSize>, out bool, out bool)` | Aligns the radix point between this instance and another floating point number. |
| `Normalize(int, bool)`                                                                                    | Normalizes the floating-point significand and adjusts the exponent.             |
| `Round(GpuBigFloatRoundingMode, bool, bool, bool)`                                                        | Applies IEEE 754 compliant rounding.                                            |
| `Add(GpuBigFloat<TSize>)`                                                                                 | Adds two `GpuBigFloat<TSize>` values.                                           |
| `Subtract(GpuBigFloat<TSize>)`                                                                            | Subtracts another `GpuBigFloat<TSize>` from this value.                         |

## Examples

### Constructing a GpuBigFloat

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math, Kassyi.GSharp.Core

```csharp


using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU)
Example.ComputeFloat();

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void ComputeFloat()
    {
        // Initialize significand representing the value '3' (zero allocation)
        var buffer = new LimbBuffer8();
        buffer[0] = 3;
        var significand = new GpuUIntBigInteger<LimbBuffer8>(buffer);

        // Create the signed big float (e.g., 3 * 2^-1 = 1.5)
        var floatVal = new GpuBigFloat<LimbBuffer8>(significand, exponent: -1, isNegative: false);

        // Evaluate if negative and compute absolute value
        if (floatVal.IsNegative)
        {
            floatVal = new GpuBigFloat<LimbBuffer8>(floatVal.UFloat, isNegative: false);
        }
        
        Console.WriteLine($"Computed Float: Significand[0]={floatVal.UFloat.Significand.Limbs[0]}, Exponent={floatVal.UFloat.Exponent}, IsNegative={floatVal.IsNegative}");
    }
}
```

## Remarks

Unlike earlier versions that required Host orchestration, `GpuBigFloat<TSize>` is a purely unmanaged structure that executes entirely on the GPU.
Because it relies on C# 12 `[InlineArray]` (such as `LimbBuffer8`), its instantiation operates strictly on the stack or registers, fully adhering to the Zero-Allocation rule. For details, see [Memory Management and Zero-Allocation](../00_index.md#memory-management-and-zero-allocation).

For more information on the separation of Signed and Unsigned components to prevent Warp Divergence, see [The Architecture: Signed vs. Unsigned Separation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md#3-the-architecture-signed-vs-unsigned-separation).

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigUFloat<TSize> Struct (G#)](GpuBigUFloat.md)
- [GpuBigFloatRoundingMode Enum (G#)](GpuBigFloatRoundingMode.md)
- [GpuBigFloat Operators (G#)](Operators.md)
