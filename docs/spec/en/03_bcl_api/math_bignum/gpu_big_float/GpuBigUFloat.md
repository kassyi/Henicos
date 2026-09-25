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
| [AlignRadixPoint(GpuBigUFloat<TSize>)](#alignradixpointgpubigufloattsize) | Aligns the radix point between this instance and another unsigned float, returning aligned values and rounding bits as a Tuple. |
| [Normalize(int, bool)](#normalizeint-bool)                                                                         | Normalizes the floating-point significand and adjusts the exponent.                 |
| [Round(GpuBigFloatRoundingMode, bool, bool, bool)](#roundgpubigfloatroundingmode-bool-bool-bool)                                                 | Applies IEEE 754 compliant rounding logic.                                          |
| [Add(GpuBigUFloat<TSize>)](#addgpubigufloattsize)                                                                            | Adds two unsigned floating point values.                                            |
| [Subtract(GpuBigUFloat<TSize>)](#subtractgpubigufloattsize)                                                                  | Subtracts another unsigned floating point value, clamping to 0 if underflow occurs. |

## Methods Detail

### AlignRadixPoint(GpuBigUFloat\<TSize\>)

Aligns the radix point between this instance and another unsigned float, returning aligned values and rounding bits.

```csharp
public (GpuBigUFloat<TSize> AlignedThis, GpuBigUFloat<TSize> AlignedOther, bool Guard, bool Round, bool StickyThis, bool StickyOther) AlignRadixPoint(GpuBigUFloat<TSize> other);
```

#### Parameters

`other` _GpuBigUFloat\<TSize\>_<br>
The other unsigned float to align against.

#### Returns

A tuple containing the aligned `this` operand, aligned `other` operand, and the preserved guard, round, and sticky bits.

### Normalize(int, bool)

Normalizes the floating-point significand and adjusts the exponent.

```csharp
public GpuBigUFloat<TSize> Normalize(int leadingZeros = 0, bool carryOut = false);
```

#### Parameters

`leadingZeros` _int_<br>
The number of leading zeros in the significand.

`carryOut` _bool_<br>
A value indicating whether a carry-out occurred in the previous operation.

#### Returns

A normalized `GpuBigUFloat<TSize>`.

### Round(GpuBigFloatRoundingMode, bool, bool, bool)

Applies IEEE 754 compliant rounding logic to the unsigned float.

```csharp
public GpuBigUFloat<TSize> Round(GpuBigFloatRoundingMode mode, bool guardBit, bool roundBit, bool stickyBit);
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

The rounded `GpuBigUFloat<TSize>`.

### Add(GpuBigUFloat\<TSize\>)

Adds two unsigned floating point values.

```csharp
public GpuBigUFloat<TSize> Add(GpuBigUFloat<TSize> other);
```

#### Parameters

`other` _GpuBigUFloat\<TSize\>_<br>
The value to add.

#### Returns

The sum as a new `GpuBigUFloat<TSize>`.

### Subtract(GpuBigUFloat\<TSize\>)

Subtracts another unsigned floating point value, clamping to 0 if underflow occurs (assumes `this >= other`).

```csharp
public GpuBigUFloat<TSize> Subtract(GpuBigUFloat<TSize> other);
```

#### Parameters

`other` _GpuBigUFloat\<TSize\>_<br>
The value to subtract.

#### Returns

The difference as a new `GpuBigUFloat<TSize>`.

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
