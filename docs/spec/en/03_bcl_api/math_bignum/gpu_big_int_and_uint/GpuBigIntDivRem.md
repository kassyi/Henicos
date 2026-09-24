---
title: "GpuBigIntDivRem Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Provides static division and remainder operations, including Newton-Raphson approximation for GpuUIntBigInteger."
---

## GpuBigIntDivRem Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Provides static division and remainder operations, including Newton-Raphson approximation for `GpuUIntBigInteger`.

```csharp
public static class GpuBigIntDivRem
```

## Methods

| Name                                                                                                                                                                 | Description                                                                      |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| [Div(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#divgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)                                            | Computes arbitrary-precision division (`leftOperand / rightOperand`) on the GPU. |
| [DivRem(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#divremgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes arbitrary-precision division and remainder simultaneously on the GPU.   |
| [GetActualBits(GpuUIntBigInteger)](#getactualbitsgpuuintbiginteger)                                                                                                  | Gets the actual bit length of the GPU unsigned big integer.                      |
| [Mod(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#modgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)                                            | Computes arbitrary-precision modulo (`leftOperand % rightOperand`) on the GPU.   |
| [ModPow(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#modpowgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes modular exponentiation (`baseVal^exp mod mod`) on the GPU.              |
| [Sqrt(GpuUIntBigInteger, GpuUIntBigInteger)](#sqrtgpuuintbiginteger-gpuuintbiginteger)                                                                               | Computes the integer square root (`floor(sqrt(leftOperand))`) on the GPU.        |

## Div(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes arbitrary-precision division (`leftOperand / rightOperand`) on the GPU.

```csharp
public static void Div(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The dividend.

`rightOperand` _GpuUIntBigInteger_<br>
The divisor.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the quotient.

## DivRem(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes arbitrary-precision division and remainder simultaneously on the GPU.

```csharp
public static void DivRem(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger quotientBuffer, GpuUIntBigInteger remainderBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The dividend.

`rightOperand` _GpuUIntBigInteger_<br>
The divisor.

`quotientBuffer` _GpuUIntBigInteger_<br>
The buffer to store the quotient.

`remainderBuffer` _GpuUIntBigInteger_<br>
The buffer to store the remainder.

#### Exceptions

`ArgumentException`<br>
Thrown when the operands and result buffers do not have the same number of words.

`DivideByZeroException`<br>
Thrown when the divisor (`rightOperand`) is zero.

## GetActualBits(GpuUIntBigInteger)

Gets the actual bit length of the GPU unsigned big integer (excluding leading zero bits).

```csharp
public static int GetActualBits(GpuUIntBigInteger gpuBigInt);
```

#### Parameters

`gpuBigInt` _GpuUIntBigInteger_<br>
The GPU unsigned big integer.

#### Returns

_int_<br>
The minimum number of bits required to represent the value.

## Mod(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes arbitrary-precision modulo (`leftOperand % rightOperand`) on the GPU.

```csharp
public static void Mod(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The dividend.

`rightOperand` _GpuUIntBigInteger_<br>
The divisor.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the remainder.

## ModPow(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes modular exponentiation (`baseVal^exp mod mod`) on the GPU.

```csharp
public static void ModPow(GpuUIntBigInteger baseVal, GpuUIntBigInteger exp, GpuUIntBigInteger mod, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`baseVal` _GpuUIntBigInteger_<br>
The base value.

`exp` _GpuUIntBigInteger_<br>
The exponent value.

`mod` _GpuUIntBigInteger_<br>
The modulus value.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Sqrt(GpuUIntBigInteger, GpuUIntBigInteger)

Computes the integer square root (`floor(sqrt(leftOperand))`) on the GPU.

```csharp
public static void Sqrt(GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to calculate the square root for.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the square root.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU)
using var context = new GpuContext(2048, GpuExecutionEngine.GSharpAot);

using var valA = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(150), minWords: 4);
using var valB = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(10), minWords: 4);

using var q = new GpuUIntBigInteger(context, 4);
using var r = new GpuUIntBigInteger(context, 4);

// Execute Division and Remainder (using Newton-Raphson approximation under the hood)
GpuBigIntDivRem.DivRem(valA, valB, q, r);

// Execute Integer Square Root
GpuBigIntDivRem.Sqrt(valA, r);

// Execute Modular Exponentiation
GpuBigIntDivRem.ModPow(valA, valB, valB, r);
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuContext](../../../03_bcl_api/math_bignum/gpu_context/GpuContext.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
