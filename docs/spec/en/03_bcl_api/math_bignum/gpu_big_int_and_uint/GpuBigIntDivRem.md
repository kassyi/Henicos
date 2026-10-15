---
title: "GpuBigIntDivRem Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides static arbitrary-precision division, remainder, modulo, Barrett reduction, and root extraction algorithms on the GPU."
---

## GpuBigIntDivRem Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides static arbitrary-precision division, remainder, modulo, Barrett reduction, and root extraction algorithms on the GPU using Newton-Raphson approximation and reciprocal multiplication.

```csharp
public static partial class GpuBigIntDivRem
```

Inheritance: `Object` $\rightarrow$ `GpuBigIntDivRem`

## Methods

| Name | Description |
| --- | --- |
| [Div(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#divgpubiguint-gpubiguint-gpubiguint) | Computes arbitrary-precision division (`leftOperand / rightOperand`) on the GPU. |
| [DivRem(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#divremgpubiguint-gpubiguint-gpubiguint-gpubiguint) | Computes arbitrary-precision division and remainder simultaneously on the GPU. |
| [GetActualBits(GpuBigUInt)](#getactualbitsgpubiguint) | Calculates the exact number of significant bits in the specified `GpuBigUInt` by inspecting the highest non-zero bit. |
| [Mod(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modgpubiguint-gpubiguint-gpubiguint) | Computes arbitrary-precision modulo (`leftOperand % rightOperand`) on the GPU. |
| [ModPow(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modpowgpubiguint-gpubiguint-gpubiguint-gpubiguint) | Computes modular exponentiation (`baseVal^exp mod mod`) on the GPU. |
| [Reduce(GpuBigUInt, GpuBigUInt, GpuBigUInt, Int32, Int32, GpuBigUInt, GpuBigUInt)](#reducegpubiguint-gpubiguint-gpubiguint-int32-int32-gpubiguint-gpubiguint) | Performs a Barrett reduction using a precomputed reciprocal. |
| [ReduceZeroSync(GpuBigUInt, GpuBigUInt, GpuBigUInt, Int32, Int32, GpuBigUInt, GpuBigUInt)](#reducezerosyncgpubiguint-gpubiguint-gpubiguint-int32-int32-gpubiguint-gpubiguint) | Performs a zero-sync Barrett reduction using a precomputed reciprocal without host synchronization (CUDA Graph safe). |
| [Sqrt(GpuBigUInt, GpuBigUInt)](#sqrtgpubiguint-gpubiguint) | Computes the integer square root (`floor(sqrt(leftOperand))`) on the GPU. |

## Div(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision division (`leftOperand / rightOperand`) on the GPU.

```csharp
public static void Div(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The dividend operand.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The divisor operand.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the quotient.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length.

`DivideByZeroException`<br>
Thrown when `rightOperand` is zero.

## DivRem(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes both the quotient and the remainder of two arbitrary-precision unsigned integers on the GPU.

```csharp
public static void DivRem(
    GpuBigUInt leftOperand,
    GpuBigUInt rightOperand,
    GpuBigUInt quotientBuffer,
    GpuBigUInt remainderBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The dividend operand ($A$).

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The divisor operand ($B$).

`quotientBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the quotient ($\lfloor A / B \rfloor$).

`remainderBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the remainder ($A \bmod B$).

#### Exceptions

`DivideByZeroException`<br>
Thrown when the divisor (`rightOperand`) is zero.

## GetActualBits(GpuBigUInt)

Calculates the exact number of significant bits in the specified `GpuBigUInt` by inspecting the highest non-zero bit.

```csharp
public static int GetActualBits(GpuBigUInt gpuBigInt);
```

#### Parameters

`gpuBigInt` _[GpuBigUInt](GpuBigUInt.md)_<br>
The big integer to inspect.

#### Returns

_Int32_<br>
The bit length of the big integer, or `0` if all limb words are zero.

## Mod(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision remainder (modulo) of two numbers on the GPU (`resultBuffer = leftOperand % rightOperand`).

```csharp
public static void Mod(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The dividend operand.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The divisor operand.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the remainder.

#### Exceptions

`ArgumentException`<br>
Thrown when the operands and result buffer do not have the same number of words.

`DivideByZeroException`<br>
Thrown when the divisor (`rightOperand`) is zero.

## ModPow(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes modular exponentiation (`baseVal^exp mod mod`) on the GPU using binary exponentiation (square-and-multiply).

```csharp
public static void ModPow(GpuBigUInt baseVal, GpuBigUInt exp, GpuBigUInt mod, GpuBigUInt resultBuffer);
```

#### Parameters

`baseVal` _[GpuBigUInt](GpuBigUInt.md)_<br>
The base value.

`exp` _[GpuBigUInt](GpuBigUInt.md)_<br>
The exponent value.

`mod` _[GpuBigUInt](GpuBigUInt.md)_<br>
The modulus value.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the modular power.

#### Exceptions

`ArgumentException`<br>
Thrown when `baseVal`, `mod`, and `resultBuffer` have mismatched word lengths.

`DivideByZeroException`<br>
Thrown when the modulus (`mod`) is zero.

## Reduce(GpuBigUInt, GpuBigUInt, GpuBigUInt, Int32, Int32, GpuBigUInt, GpuBigUInt)

Performs a Barrett reduction using a precomputed reciprocal.

```csharp
public static void Reduce(
    GpuBigUInt leftOperand,
    GpuBigUInt rightOperand,
    GpuBigUInt reciprocalOfB,
    int targetBits,
    int aActualWords,
    GpuBigUInt quotientBuffer,
    GpuBigUInt remainderBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The dividend.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The divisor.

`reciprocalOfB` _[GpuBigUInt](GpuBigUInt.md)_<br>
The precomputed reciprocal of the divisor.

`targetBits` _Int32_<br>
The target precision in bits.

`aActualWords` _Int32_<br>
The actual number of words in the dividend.

`quotientBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The buffer to store the computed quotient.

`remainderBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The buffer to store the computed remainder.

## ReduceZeroSync(GpuBigUInt, GpuBigUInt, GpuBigUInt, Int32, Int32, GpuBigUInt, GpuBigUInt)

Performs a zero-sync Barrett reduction using a precomputed reciprocal. This method does not synchronize with the CPU for remainder correction, enabling CUDA Graph capture.

```csharp
public static void ReduceZeroSync(
    GpuBigUInt leftOperand,
    GpuBigUInt rightOperand,
    GpuBigUInt reciprocalOfB,
    int targetBits,
    int aActualWords,
    GpuBigUInt quotientBuffer,
    GpuBigUInt remainderBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The left operand (dividend).

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The right operand (divisor).

`reciprocalOfB` _[GpuBigUInt](GpuBigUInt.md)_<br>
The precomputed reciprocal of the right operand.

`targetBits` _Int32_<br>
The target precision in bits.

`aActualWords` _Int32_<br>
The actual word count of the left operand.

`quotientBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The buffer to store the quotient result.

`remainderBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The buffer to store the remainder result.

## Sqrt(GpuBigUInt, GpuBigUInt)

Computes the integer square root ($\lfloor\sqrt{\text{leftOperand}}\rfloor$) on the GPU using inverse square root approximation and Heron's refinement.

```csharp
public static void Sqrt(GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The non-negative big integer operand.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the integer square root.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(2048, GpuExecutionEngine.GSharpAot);

using var valA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(155), minWords: 4);
using var valB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(10), minWords: 4);

using var q = new GpuBigUInt(workspace, 4);
using var r = new GpuBigUInt(workspace, 4);

// Execute Division and Remainder (using Newton-Raphson approximation under the hood)
GpuBigIntDivRem.DivRem(valA, valB, q, r);
Console.WriteLine($"Quotient={q.ToBigInteger(false)}");
Console.WriteLine($"Remainder={r.ToBigInteger(false)}");

// Execute Integer Square Root
GpuBigIntDivRem.Sqrt(valA, r);
Console.WriteLine($"Sqrt={r.ToBigInteger(false)}");

// Execute Modular Exponentiation
GpuBigIntDivRem.ModPow(valA, valB, valB, r);
Console.WriteLine($"ModPow={r.ToBigInteger(false)}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
