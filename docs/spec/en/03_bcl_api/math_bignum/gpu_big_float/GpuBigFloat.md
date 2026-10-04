---
title: "GpuBigFloat<TSize> Struct (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Reference for arbitrary-precision floating-point numbers on the GPU."
---

## GpuBigFloat<TSize> Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

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
| `GpuBigFloat(GpuBigUInt<TSize>, int, bool)` | Initializes a new floating point wrapper from significand, exponent, and sign flag. |

## Properties / Fields

| Name         | Description                                                                   |
| ------------ | ----------------------------------------------------------------------------- |
| `UFloat`     | The underlying unsigned floating-point value payload (`GpuBigUFloat<TSize>`). |
| `IsNegative` | `true` if the floating-point number is strictly negative.                     |

## Methods

| Name                                                                                             | Description                                                                                                                            |
| ------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------- |
| [AlignRadixPoint(GpuBigFloat<TSize>)](#alignradixpointgpubigfloattsize)                          | Aligns the radix point between this instance and another floating point number, returning aligned values and rounding bits as a Tuple. |
| [Normalize(int, bool)](#normalizeint-bool)                                                       | Normalizes the floating-point significand and adjusts the exponent.                                                                    |
| [Round(GpuBigFloatRoundingMode, bool, bool, bool)](#roundgpubigfloatroundingmode-bool-bool-bool) | Applies IEEE 754 compliant rounding.                                                                                                   |
| [Add(GpuBigFloat<TSize>)](#addgpubigfloattsize)                                                  | Adds two GpuBigFloat<TSize> values.                                                                                                    |
| [Subtract(GpuBigFloat<TSize>)](#subtractgpubigfloattsize)                                        | Subtracts another GpuBigFloat<TSize> from this value.                                                                                  |

## Methods Detail

### AlignRadixPoint(GpuBigFloat\<TSize\>)

Aligns the radix point between this instance and another floating point number, returning aligned values and rounding bits.

```csharp
public (GpuBigFloat<TSize> AlignedThis, GpuBigFloat<TSize> AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther) AlignRadixPoint(GpuBigFloat<TSize> other);
```

#### Parameters

`other` _GpuBigFloat\<TSize\>_<br>
The other floating point number to align against.

#### Returns

A tuple containing the aligned `this` operand, aligned `other` operand, and the preserved guard, round, and sticky bits.

### Normalize(int, bool)

Normalizes the floating-point significand and adjusts the exponent.

```csharp
public GpuBigFloat<TSize> Normalize(int leadingZeros = 0, bool carryOut = false);
```

#### Parameters

`leadingZeros` _int_<br>
The number of leading zeros in the significand.

`carryOut` _bool_<br>
A value indicating whether a carry-out occurred in the previous operation.

#### Returns

A normalized `GpuBigFloat<TSize>`.

### Round(GpuBigFloatRoundingMode, bool, bool, bool)

Applies IEEE 754 compliant rounding.

```csharp
public GpuBigFloat<TSize> Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit);
```

#### Parameters

`mode` _GpuBigFloatRoundingMode_<br>
The IEEE 754 rounding mode to apply.

`guardBit` _bool_<br>
The guard bit.

`roundBit` _bool_<br>
The round bit.

`stickyBit` _bool_<br>
The sticky bit.

#### Returns

The rounded `GpuBigFloat<TSize>`.

### Add(GpuBigFloat\<TSize\>)

Adds two `GpuBigFloat<TSize>` values.

```csharp
public GpuBigFloat<TSize> Add(GpuBigFloat<TSize> other);
```

#### Parameters

`other` _GpuBigFloat\<TSize\>_<br>
The value to add.

#### Returns

The sum as a new `GpuBigFloat<TSize>`.

### Subtract(GpuBigFloat\<TSize\>)

Subtracts another `GpuBigFloat<TSize>` value.

```csharp
public GpuBigFloat<TSize> Subtract(GpuBigFloat<TSize> other);
```

#### Parameters

`other` _GpuBigFloat\<TSize\>_<br>
The value to subtract.

#### Returns

The difference as a new `GpuBigFloat<TSize>`.

## Examples

### Constructing a GpuBigFloat

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath, Kassyi.GSharp.Core

```csharp


using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

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
        var significand = new GpuBigUInt<LimbBuffer8>(buffer);

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
