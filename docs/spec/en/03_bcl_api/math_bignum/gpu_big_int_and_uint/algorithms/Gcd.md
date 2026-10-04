---
title: "GpuBigInt.Gcd Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for calculating the Greatest Common Divisor (GCD) using Lehmer's algorithm on the GPU."
---

## GpuBigIntMath.Gcd Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Calculates the Greatest Common Divisor (GCD) of two arbitrary-precision integers using Lehmer's algorithm.

## Overloads

| Name                                                                                                                       | Description                                                  |
| -------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------ |
| [Gcd(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcd-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes the greatest common divisor of two absolute values. |

## Gcd(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Executes Lehmer's GCD algorithm natively on the GPU buffer.

```csharp
public static void Gcd(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
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
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)

using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(1024);

var aVal = GpuBigUInt.FromBigInteger(workspace, BigInteger.Parse("123456789"));
var bVal = GpuBigUInt.FromBigInteger(workspace, BigInteger.Parse("987654321"));

var gpuA = new GpuBigInt(aVal, false);
var gpuB = new GpuBigInt(bVal, false);

using var result = new GpuBigUInt(workspace, aVal.NumWords);

// Calculate GCD on the GPU
GpuBigIntMath.Gcd(gpuA.Value, gpuB.Value, result);

Console.WriteLine($"GCD Result Size: {result.NumWords} words.");
```

## Remarks

This method implements Lehmer's GCD algorithm. It relies heavily on 64-bit multi-word arithmetic on the GPU. Because this operation requires substantial temporary memory for intermediate divisions, it uses the `GpuWorkspace` memory pool instead of `cudaMalloc`.

## HalfGcdCpuReference Class

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`

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
- [GpuBigInt and GpuBigUInt Operators (G#)](../Operators.md)

W
