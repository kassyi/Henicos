---
title: "GpuBigIntMath Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Provides static mathematical and bitwise operations for GpuBigUInt on the GPU."
---

## GpuBigIntMath Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides static mathematical and bitwise operations for `GpuBigUInt` on the GPU.

```csharp
public static class GpuBigIntMath
```

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes arbitrary-precision addition (`leftOperand + rightOperand`) on the GPU. |
| [Add(GpuBigUInt, uint, GpuBigUInt)](#addgpuuintbiginteger-uint-gpuuintbiginteger) | Adds a scalar 32-bit unsigned integer to `leftOperand` on the GPU. |
| [Sub(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes arbitrary-precision subtraction (`leftOperand - rightOperand`) on the GPU. |
| [Sub(GpuBigUInt, uint, GpuBigUInt)](#subgpuuintbiginteger-uint-gpuuintbiginteger) | Subtracts a scalar 32-bit unsigned integer from `leftOperand` on the GPU. |
| [Mul(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes full arbitrary-precision multiplication (`leftOperand * rightOperand`) on the GPU. |
| [Mul(GpuBigUInt, uint, GpuBigUInt)](#mulgpuuintbiginteger-uint-gpuuintbiginteger) | Multiplies `leftOperand` by a scalar 32-bit unsigned integer on the GPU. |
| [Gcd(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Computes the Greatest Common Divisor (GCD) of two big integers on the GPU. |
| [ShiftLeft(GpuBigUInt, int, GpuBigUInt)](#shiftleftgpuuintbiginteger-int-gpuuintbiginteger) | Shifts the big integer left by the specified bit count on the GPU. |
| [ShiftRight(GpuBigUInt, int, GpuBigUInt)](#shiftrightgpuuintbiginteger-int-gpuuintbiginteger) | Shifts the big integer right by the specified bit count on the GPU. |
| [BitwiseAnd(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Performs bitwise AND on the GPU. |
| [BitwiseOr(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Performs bitwise OR on the GPU. |
| [BitwiseXor(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpuuintbiginteger-gpuuintbiginteger-gpuuintbiginteger) | Performs bitwise XOR on the GPU. |
| [BitwiseNot(GpuBigUInt, GpuBigUInt)](#bitwisenotgpuuintbiginteger-gpuuintbiginteger) | Performs bitwise NOT on the GPU. |
| [IsLessThan(GpuBigUInt, GpuBigUInt)](#islessthangpuuintbiginteger-gpuuintbiginteger) | Determines whether `leftOperand` is strictly less than `rightOperand` on the GPU. |
| [Pow10(GpuWorkspace, int)](#pow10gpuworkspace-int) | Computes 10 raised to the specified exponent on the GPU using exponentiation by squaring. |

## Add(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision addition (`leftOperand + rightOperand`) on the GPU.

```csharp
public static void Add(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## Add(GpuBigUInt, uint, GpuBigUInt)

Adds a scalar 32-bit unsigned integer to `leftOperand` on the GPU.

```csharp
public static void Add(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _uint_<br>
The scalar value to add.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Sub(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision subtraction (`leftOperand - rightOperand`) on the GPU.

```csharp
public static void Sub(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to subtract from.

`rightOperand` _GpuUIntBigInteger_<br>
The operand to subtract.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## Sub(GpuBigUInt, uint, GpuBigUInt)

Subtracts a scalar 32-bit unsigned integer from `leftOperand` on the GPU.

```csharp
public static void Sub(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to subtract from.

`rightOperand` _uint_<br>
The scalar value to subtract.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Mul(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes full arbitrary-precision multiplication (`leftOperand * rightOperand`) on the GPU.

```csharp
public static void Mul(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result. Must have a length equal to the sum of the number of words in both operands.

#### Exceptions

`ArgumentException`<br>
Thrown when `resultBuffer.NumWords` is not equal to `leftOperand.NumWords + rightOperand.NumWords`.

## Mul(GpuBigUInt, uint, GpuBigUInt)

Multiplies `leftOperand` by a scalar 32-bit unsigned integer on the GPU.

```csharp
public static void Mul(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _uint_<br>
The scalar value to multiply by.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

## Gcd(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the Greatest Common Divisor (GCD) of two big integers on the GPU.

```csharp
public static void Gcd(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the computed GCD.

## ShiftLeft(GpuBigUInt, int, GpuBigUInt)

Shifts the big integer left by the specified bit count on the GPU.

```csharp
public static void ShiftLeft(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to shift.

`shiftBits` _int_<br>
The number of bits to shift left.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the shifted result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## ShiftRight(GpuBigUInt, int, GpuBigUInt)

Shifts the big integer right by the specified bit count on the GPU.

```csharp
public static void ShiftRight(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to shift.

`shiftBits` _int_<br>
The number of bits to shift right.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the shifted result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## BitwiseAnd(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Performs bitwise AND on the GPU.

```csharp
public static void BitwiseAnd(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## BitwiseOr(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Performs bitwise OR on the GPU.

```csharp
public static void BitwiseOr(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## BitwiseXor(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Performs bitwise XOR on the GPU.

```csharp
public static void BitwiseXor(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## BitwiseNot(GpuBigUInt, GpuBigUInt)

Performs bitwise NOT on the GPU.

```csharp
public static void BitwiseNot(GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The operand to invert.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to store the result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## IsLessThan(GpuBigUInt, GpuBigUInt)

Determines whether `leftOperand` is strictly less than `rightOperand` on the GPU.

```csharp
public static bool IsLessThan(GpuBigUInt leftOperand, GpuBigUInt rightOperand);
```

#### Parameters

`leftOperand` _GpuUIntBigInteger_<br>
The first operand.

`rightOperand` _GpuUIntBigInteger_<br>
The second operand.

#### Returns

_bool_<br>
`true` if `leftOperand` is less than `rightOperand`; otherwise, `false`.

## Pow10(GpuWorkspace, int)

Computes 10 raised to the specified exponent on the GPU using exponentiation by squaring.

```csharp
public static GpuBigUInt Pow10(GpuWorkspace workspace, int exponent);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`exponent` _int_<br>
The exponent.

#### Returns

_GpuUIntBigInteger_<br>
A new `GpuBigUInt` containing the result.

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown if the exponent is negative or exceeds the maximum supported value.

## Examples

### Basic Arithmetic Operations on the GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

// Setup initial values
using var valA = GpuBigUInt.FromBigInteger(workspace, new BigInteger(150), minWords: 4);
using var valB = GpuBigUInt.FromBigInteger(workspace, new BigInteger(50), minWords: 4);
using var valSum = new GpuBigUInt(workspace, 4);

// Execute addition
GpuBigIntMath.Add(valA, valB, valSum);

// Execute subtraction
GpuBigIntMath.Sub(valA, valB, valSum);
Console.WriteLine($"Sub={valSum.ToBigInteger(false)}");

// Execute multiplication (needs sum of words for buffer)
using var valMul = new GpuBigUInt(workspace, 8);
GpuBigIntMath.Mul(valA, valB, valMul);
Console.WriteLine($"Mul={valMul.ToBigInteger(false)}");
```

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigUInt](GpuBigUInt.md)
- [GpuBigUInt<TSize>](GpuUIntBigInteger_Generic.md)
