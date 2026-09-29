---
title: "GpuBigInteger.Gcd Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for calculating the Greatest Common Divisor (GCD) using Lehmer's algorithm on the GPU."
---

## GpuBigIntMath.Gcd Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Calculates the Greatest Common Divisor (GCD) of two arbitrary-precision integers using Lehmer's algorithm.

## Overloads

| Name                                                                                                                       | Description                                                  |
| -------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| [Gcd(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#gcd-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes the greatest common divisor of two absolute values. |

## Gcd(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Executes Lehmer's GCD algorithm natively on the GPU buffer.

```csharp
public static void Gcd(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first absolute value.

`rightOperand` _GpuUIntBigInteger_<br>
The second absolute value.

`resultBuffer` _GpuUIntBigInteger_<br>
The pre-allocated buffer where the resulting greatest common divisor will be written.

#### Returns

This method does not return a value. The output is stored directly in the `resultBuffer`.

## Examples

### Calculating GCD of massive numbers

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math

```csharp
// Execution Context: Host (CPU)

using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;

using var workspace = new GpuWorkspace(1024);

var aVal = GpuUIntBigInteger.FromBigInteger(workspace, BigInteger.Parse("123456789"));
var bVal = GpuUIntBigInteger.FromBigInteger(workspace, BigInteger.Parse("987654321"));

var gpuA = new GpuBigInteger(aVal, false);
var gpuB = new GpuBigInteger(bVal, false);

using var result = new GpuUIntBigInteger(workspace, aVal.NumWords);

// Calculate GCD on the GPU
GpuBigIntMath.Gcd(gpuA.Value, gpuB.Value, result);

Console.WriteLine($"GCD Result Size: {result.NumWords} words.");
```

## Remarks

This method implements Lehmer's GCD algorithm. It relies heavily on 64-bit multi-word arithmetic on the GPU. Because this operation requires substantial temporary memory for intermediate divisions, it uses the `GpuWorkspace` memory pool instead of `cudaMalloc`.

## HalfGcdCpuReference Class

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Algorithms`

A CPU reference implementation of Lehmer's / Half-GCD algorithm is provided for host-side validation or testing prior to GPU execution.

```csharp
public static class HalfGcdCpuReference
{
    public static BigInteger Gcd(BigInteger a, BigInteger b);
}
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuBigInteger and GpuUIntBigInteger Operators (G#)](../Operators.md)

W
