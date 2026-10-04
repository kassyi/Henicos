---
title: "GpuBigIntDivRem Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Provides static division and remainder operations, including Newton-Raphson approximation for GpuBigUInt."
---

## GpuBigIntDivRem Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides static division and remainder operations, including Newton-Raphson approximation for `GpuBigUInt`.

```csharp
public static class GpuBigIntDivRem
```

## Methods

| Name                                                                                                                                                                 | Description                                                                      |
| -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------- |
| [Div(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#divgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)                                            | Computes arbitrary-precision division (`leftOperand / rightOperand`) on the GPU. |
| [DivRem(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#divremgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes arbitrary-precision division and remainder simultaneously on the GPU.   |
| [GetActualBits(GpuBigUInt)](#getactualbitsgpuuintbiginteger)                                                                                                  | Gets the actual bit length of the GPU unsigned big integer.                      |
| [Mod(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger)                                            | Computes arbitrary-precision modulo (`leftOperand % rightOperand`) on the GPU.   |
| [ModPow(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modpowgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes modular exponentiation (`baseVal^exp mod mod`) on the GPU.              |
| [Sqrt(GpuBigUInt, GpuBigUInt)](#sqrtgpuuintbiginteger-gpuuintbiginteger)                                                                               | Computes the integer square root (`floor(sqrt(leftOperand))`) on the GPU.        |

## Div(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision division (`leftOperand / rightOperand`) on the GPU.

```csharp
public static void Div(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The dividend.

`rightOperand` _GpuUIntBigInteger_<br>
The divisor.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the quotient.

## DivRem(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision division and remainder simultaneously on the GPU.

```csharp
public static void DivRem(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt quotientBuffer, GpuBigUInt remainderBuffer);
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

## GetActualBits(GpuBigUInt)

Gets the actual bit length of the GPU unsigned big integer (excluding leading zero bits).

```csharp
public static int GetActualBits(GpuBigUInt gpuBigInt);
```

#### Parameters

`gpuBigInt` _GpuUIntBigInteger_<br>
The GPU unsigned big integer.

#### Returns

_int_<br>
The minimum number of bits required to represent the value.

#### Exceptions

None. Returns `0` if all limb words are zero.

## Mod(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision modulo (`leftOperand % rightOperand`) on the GPU.

```csharp
public static void Mod(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The dividend.

`rightOperand` _GpuUIntBigInteger_<br>
The divisor.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the remainder.

#### Exceptions

`ArgumentException`<br>
Thrown when the operands and result buffer do not have the same number of words.

`DivideByZeroException`<br>
Thrown when the divisor (`rightOperand`) is zero.

## ModPow(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes modular exponentiation (`baseVal^exp mod mod`) on the GPU.

```csharp
public static void ModPow(GpuBigUInt baseVal, GpuBigUInt exp, GpuBigUInt mod, GpuBigUInt resultBuffer);
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

#### Exceptions

`ArgumentException`<br>
Thrown when operand and result buffer word counts do not match.

`DivideByZeroException`<br>
Thrown when the modulus (`mod`) is zero.

## Sqrt(GpuBigUInt, GpuBigUInt)

Computes the integer square root (`floor(sqrt(leftOperand))`) on the GPU.

```csharp
public static void Sqrt(GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to calculate the square root for.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the square root.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(2048, GpuExecutionEngine.GSharpAot);

using var valA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(150), minWords: 4);
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
