---
title: "GpuBigIntDivRem.DivRem Method (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Reference for calculating Quotient and Remainder simultaneously on the GPU."
---

## GpuBigIntDivRem.DivRem Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

When performing division (`/`) or modulo (`%`) on massive multi-precision integers, the computational cost is extremely high. Often, an algorithm requires both the quotient and the remainder simultaneously (e.g., base conversion, Euclidean algorithms).

[`GpuBigIntDivRem.DivRem`](../GpuBigIntDivRem.md) calculates and writes both values in a single Newton-Raphson reciprocal pass on the GPU, cutting execution time in half compared to calling `Div` and `Mod` separately.

## Overloads

| Name | Description |
| --- | --- |
| [DivRem(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#divremgpubiguintview-gpubiguintview-gpubiguintview-gpubiguintview) | Computes both the quotient and the remainder of two arbitrary-precision unsigned integers on the GPU. |

## DivRem(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Computes both the quotient and the remainder of two arbitrary-precision unsigned integers on the GPU.

```csharp
public static void DivRem(
    scoped GpuBigUIntView leftOperand,
    scoped GpuBigUIntView rightOperand,
    scoped GpuBigUIntView quotientBuffer,
    scoped GpuBigUIntView remainderBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The dividend operand ($A$).

`rightOperand` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The divisor operand ($B$).

`quotientBuffer` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The destination buffer receiving the quotient ($\lfloor A / B \rfloor$).

`remainderBuffer` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The destination buffer receiving the remainder ($A \bmod B$).

#### Exceptions

`DivideByZeroException`<br>
Thrown when `rightOperand` is zero.

## Examples

### Simultaneous Division and Remainder

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(2048, GpuExecutionEngine.GSharpAot);

using var dividend = GpuBigUInt.FromBigInteger(workspace, new BigInteger(123456789), minWords: 4);
using var divisor = GpuBigUInt.FromBigInteger(workspace, new BigInteger(10000), minWords: 4);

using var quotient = new GpuBigUInt(workspace, 4);
using var remainder = new GpuBigUInt(workspace, 4);

// Execute division and remainder simultaneously in a single Newton-Raphson pass
GpuBigIntDivRem.DivRem(dividend, divisor, quotient, remainder);

Console.WriteLine($"Q: {quotient.ToBigInteger(false)}, R: {remainder.ToBigInteger(false)}");
```

## See also

### BCL API > Math Bignum

- [GpuBigIntDivRem Class (G#)](../GpuBigIntDivRem.md)
- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuBigInt and GpuBigUInt Operators (G#)](../Operators.md)
