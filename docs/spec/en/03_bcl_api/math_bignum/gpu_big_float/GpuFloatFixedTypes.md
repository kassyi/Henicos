---
title: "Fixed-Precision GPU Floats (128/256/512)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "High-level fixed-size arbitrary-precision floating-point wrappers for zero-allocation GPU execution."
---

# Fixed-Precision GPU Floats (128/256/512)

The `Kassyi.GSharp.Stdlib.Math.BigFloat` namespace provides several high-level wrapper structs for `GpuBigFloat<TSize>`, which make it easier to work with fixed-precision arbitrary-precision floats without having to specify the generic `TSize` limb buffer type explicitly.

Currently, the following fixed-precision structs are supported:

- **`GpuFloat128`**: 128-bit floating-point (wraps `GpuBigFloat<LimbBuffer4>`)
- **`GpuFloat256`**: 256-bit floating-point (wraps `GpuBigFloat<LimbBuffer8>`)
- **`GpuFloat512`**: 512-bit floating-point (wraps `GpuBigFloat<LimbBuffer16>`)

## Definition

```csharp
public struct GpuFloat128
public struct GpuFloat256
public struct GpuFloat512
```

## Constructors

| Name                                                                           | Description                                                                                           |
| ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| [GpuFloat128(GpuUInt128, int, bool)](#gpufloat128gpuuint128-int-bool)          | Initializes a new instance of the 128-bit GPU float with the specified magnitude, exponent, and sign. |
| [GpuFloat128(GpuBigFloat\<LimbBuffer4\>)`](#gpufloat128gpubigfloatlimbbuffer4) | Initializes a new wrapper instance from an existing generic 128-bit big float.                        |

> [!NOTE]
> Identical constructors exist for `GpuFloat256` (using `GpuUInt256` and `LimbBuffer8`) and `GpuFloat512` (using `GpuUInt512` and `LimbBuffer16`).

## GpuFloat128(GpuUInt128, int, bool)

Initializes a new instance of the 128-bit GPU float with the specified unsigned magnitude, exponent (base-2), and sign.

```csharp
public GpuFloat128(GpuUInt128 significand, int exponent, bool isNegative);
```

#### Parameters

`significand` _GpuUInt128_<br>
The unsigned magnitude of the number.

`exponent` _int_<br>
The base-2 exponent.

`isNegative` _bool_<br>
`true` if the number is negative; otherwise, `false`.

## GpuFloat128(GpuBigFloat\<LimbBuffer4\>)

Initializes a new wrapper instance from an existing generic 128-bit big float.

```csharp
public GpuFloat128(GpuBigFloat<LimbBuffer4> value);
```

#### Parameters

`value` _GpuBigFloat\<LimbBuffer4\>_<br>
The generic 128-bit floating point structure.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math, Kassyi.GSharp.Core

These structs fully support C# 12 collection expressions to initialize their unsigned significand with zero heap allocation on the GPU.

```csharp

using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math.BigFloat;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU)
Example.ComputeFloat128();

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void ComputeFloat128()
    {
        // Zero-allocation initialization using C# 12 collection expressions
        GpuUInt128 sigA = [5];
        GpuUInt128 sigB = [10];

        // Initialize floats: 5 * 2^-1 = 2.5, and 10 * 2^0 = 10.0
        var floatA = new GpuFloat128(sigA, exponent: -1, isNegative: false);
        var floatB = new GpuFloat128(sigB, exponent: 0, isNegative: false);

        // Perform addition
        var sum = floatA + floatB;
        
        Console.WriteLine($"Computed Float128 Sum: Significand[0]={sum.Value.UFloat.Significand.Limbs[0]}, Exponent={sum.Value.UFloat.Exponent}, IsNegative={sum.Value.IsNegative}");
    }
}
```

## Remarks

These wrappers are fully unmanaged `struct` types designed for the GPU-Direct model. They rely on the `[InlineArray]` features of their underlying limb buffers to provide zero-allocation math. For more details, see [Memory Management and Zero-Allocation](../00_index.md#memory-management-and-zero-allocation).

## See also

- [`GpuBigFloat<TSize>`](GpuBigFloat.md)
- [`GpuBigUFloat<TSize>`](GpuBigUFloat.md)
