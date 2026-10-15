---
title: "GpuBigFloatRoundingMode Enum (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Reference for IEEE 754 compliant rounding modes in arbitrary-precision GPU floating-point operations."
---

## GpuBigFloatRoundingMode Enum

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Specifies the IEEE 754 compliant rounding mode for arbitrary-precision GPU floating-point operations.

```csharp
public enum GpuBigFloatRoundingMode
```

## Members

| Member Name | Value | Description |
| --- | --- | --- |
| [`RoundToNearestEven`](#roundtonearesteven) | `0` | Round to nearest, with ties rounding to the nearest even integer significand (IEEE 754 default). |
| [`RoundTowardZero`](#roundtowardzero) | `1` | Round toward zero (truncation). |
| [`RoundTowardPositiveInfinity`](#roundtowardpositiveinfinity) | `2` | Round toward positive infinity. |
| [`RoundTowardNegativeInfinity`](#roundtowardnegativeinfinity) | `3` | Round toward negative infinity. |

## Members Detail

### RoundToNearestEven

`0`

Round to nearest, with ties rounding to the nearest even integer significand (IEEE 754 default).

### RoundTowardZero

`1`

Round toward zero (truncation).

### RoundTowardPositiveInfinity

`2`

Round toward positive infinity.

### RoundTowardNegativeInfinity

`3`

Round toward negative infinity.

## Examples

### Specifying an IEEE 754 Rounding Mode

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

var mode = GpuBigFloatRoundingMode.RoundToNearestEven;

string description = mode switch
{
    GpuBigFloatRoundingMode.RoundToNearestEven => "IEEE 754 default (ties to even)",
    GpuBigFloatRoundingMode.RoundTowardZero => "Truncation toward zero",
    GpuBigFloatRoundingMode.RoundTowardPositiveInfinity => "Ceiling toward +Infinity",
    GpuBigFloatRoundingMode.RoundTowardNegativeInfinity => "Floor toward -Infinity",
    _ => "Unknown"
};

Console.WriteLine($"Mode: {mode} ({(int)mode}) - {description}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigFloat Struct (G#)](GpuBigFloat.md)
- [GpuBigUFloat Struct (G#)](GpuBigUFloat.md)
