---
title: "GpuBigIntMath Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Provides static mathematical and bitwise operations for GpuUIntBigInteger on the GPU."
---

## GpuBigIntMath Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Provides static mathematical and bitwise operations for `GpuUIntBigInteger` on the GPU.

```csharp
public static class GpuBigIntMath
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#addgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes arbitrary-precision addition (`leftOperand + rightOperand`) on the GPU. |
| [Add(GpuUIntBigInteger, uint, GpuUIntBigInteger)](#addgpuuintbiginteger-uint-gpuuintbiginteger) | Adds a scalar 32-bit unsigned integer to `leftOperand` on the GPU. |
| [Sub(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#subgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes arbitrary-precision subtraction (`leftOperand - rightOperand`) on the GPU. |
| [Sub(GpuUIntBigInteger, uint, GpuUIntBigInteger)](#subgpuuintbiginteger-uint-gpuuintbiginteger) | Subtracts a scalar 32-bit unsigned integer from `leftOperand` on the GPU. |
| [Mul(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#mulgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes full arbitrary-precision multiplication (`leftOperand * rightOperand`) on the GPU. |
| [Mul(GpuUIntBigInteger, uint, GpuUIntBigInteger)](#mulgpuuintbiginteger-uint-gpuuintbiginteger) | Multiplies `leftOperand` by a scalar 32-bit unsigned integer on the GPU. |
| [Gcd(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#gcdgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes the Greatest Common Divisor (GCD) of two big integers on the GPU. |
| [ShiftLeft(GpuUIntBigInteger, int, GpuUIntBigInteger)](#shiftleftgpuuintbiginteger-int-gpuuintbiginteger) | Shifts the big integer left by the specified bit count on the GPU. |
| [ShiftRight(GpuUIntBigInteger, int, GpuUIntBigInteger)](#shiftrightgpuuintbiginteger-int-gpuuintbiginteger) | Shifts the big integer right by the specified bit count on the GPU. |
| [BitwiseAnd(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwiseandgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Performs bitwise AND on the GPU. |
| [BitwiseOr(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwiseorgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Performs bitwise OR on the GPU. |
| [BitwiseXor(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#bitwisexorgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Performs bitwise XOR on the GPU. |
| [BitwiseNot(GpuUIntBigInteger, GpuUIntBigInteger)](#bitwisenotgpuuintbiginteger-gpuuintbiginteger) | Performs bitwise NOT on the GPU. |
| [IsLessThan(GpuUIntBigInteger, GpuUIntBigInteger)](#islessthangpuuintbiginteger-gpuuintbiginteger) | Determines whether `leftOperand` is strictly less than `rightOperand` on the GPU. |
| [Pow10(GpuContext, int)](#pow10gpucontext-int) | Computes 10 raised to the specified exponent on the GPU using exponentiation by squaring. |

## Add(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes arbitrary-precision addition (`leftOperand + rightOperand`) on the GPU.

```csharp
public static void Add(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Add(GpuUIntBigInteger, uint, GpuUIntBigInteger)

Adds a scalar 32-bit unsigned integer to `leftOperand` on the GPU.

```csharp
public static void Add(GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _uint_<br>
The scalar value to add.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Sub(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes arbitrary-precision subtraction (`leftOperand - rightOperand`) on the GPU.

```csharp
public static void Sub(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to subtract from.

`rightOperand` _GpuUIntBigInteger_<br>
The operand to subtract.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Sub(GpuUIntBigInteger, uint, GpuUIntBigInteger)

Subtracts a scalar 32-bit unsigned integer from `leftOperand` on the GPU.

```csharp
public static void Sub(GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to subtract from.

`rightOperand` _uint_<br>
The scalar value to subtract.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Mul(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes full arbitrary-precision multiplication (`leftOperand * rightOperand`) on the GPU.

```csharp
public static void Mul(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result. Must have a length equal to the sum of the number of words in both operands.

## Mul(GpuUIntBigInteger, uint, GpuUIntBigInteger)

Multiplies `leftOperand` by a scalar 32-bit unsigned integer on the GPU.

```csharp
public static void Mul(GpuUIntBigInteger leftOperand, uint rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _uint_<br>
The scalar value to multiply by.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Gcd(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes the Greatest Common Divisor (GCD) of two big integers on the GPU.

```csharp
public static void Gcd(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the computed GCD.

## ShiftLeft(GpuUIntBigInteger, int, GpuUIntBigInteger)

Shifts the big integer left by the specified bit count on the GPU.

```csharp
public static void ShiftLeft(GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to shift.

`shiftBits` _int_<br>
The number of bits to shift left.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the shifted result.

## ShiftRight(GpuUIntBigInteger, int, GpuUIntBigInteger)

Shifts the big integer right by the specified bit count on the GPU.

```csharp
public static void ShiftRight(GpuUIntBigInteger leftOperand, int shiftBits, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to shift.

`shiftBits` _int_<br>
The number of bits to shift right.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the shifted result.

## BitwiseAnd(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Performs bitwise AND on the GPU.

```csharp
public static void BitwiseAnd(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseOr(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Performs bitwise OR on the GPU.

```csharp
public static void BitwiseOr(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseXor(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Performs bitwise XOR on the GPU.

```csharp
public static void BitwiseXor(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## BitwiseNot(GpuUIntBigInteger, GpuUIntBigInteger)

Performs bitwise NOT on the GPU.

```csharp
public static void BitwiseNot(GpuUIntBigInteger leftOperand, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to invert.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## IsLessThan(GpuUIntBigInteger, GpuUIntBigInteger)

Determines whether `leftOperand` is strictly less than `rightOperand` on the GPU.

```csharp
public static bool IsLessThan(GpuUIntBigInteger leftOperand, GpuUIntBigInteger rightOperand);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

#### Returns

_bool_<br>
`true` if `leftOperand` is less than `rightOperand`; otherwise, `false`.

## Pow10(GpuContext, int)

Computes 10 raised to the specified exponent on the GPU using exponentiation by squaring.

```csharp
public static GpuUIntBigInteger Pow10(GpuContext context, int exponent);
```

#### Parameters

`context` _GpuContext_<br>
The GPU execution context.

`exponent` _int_<br>
The exponent.

#### Returns

_GpuUIntBigInteger_<br>
A new `GpuUIntBigInteger` containing the result.

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown if the exponent is negative or exceeds the maximum supported value.

## Examples

### Basic Arithmetic Operations on the GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU)
using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);

// Setup initial values
using var valA = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(150), minWords: 4);
using var valB = GpuUIntBigInteger.FromBigInteger(context, new BigInteger(50), minWords: 4);
using var valSum = new GpuUIntBigInteger(context, 4);

// Execute addition
GpuBigIntMath.Add(valA, valB, valSum);

// Execute subtraction
GpuBigIntMath.Sub(valA, valB, valSum);
Console.WriteLine($"Sub={valSum.ToBigInteger(false)}");

// Execute multiplication (needs sum of words for buffer)
using var valMul = new GpuUIntBigInteger(context, 8);
GpuBigIntMath.Mul(valA, valB, valMul);
Console.WriteLine($"Mul={valMul.ToBigInteger(false)}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuUIntBigInteger](GpuUIntBigInteger.md)
- [GpuUIntBigInteger<TSize>](GpuUIntBigInteger_Generic.md)
