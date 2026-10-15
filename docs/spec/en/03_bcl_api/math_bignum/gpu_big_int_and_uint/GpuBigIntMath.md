---
title: "GpuBigIntMath Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides static mathematical and bitwise operations for GpuBigUInt and arbitrary-precision integer calculations on the GPU."
---

## GpuBigIntMath Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides static mathematical and bitwise operations for [`GpuBigUInt`](GpuBigUInt.md) and arbitrary-precision integer calculations on the GPU.

```csharp
public static class GpuBigIntMath
```

Inheritance: `Object` $\rightarrow$ `GpuBigIntMath`

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#addgpubiguint-gpubiguint-gpubiguint) | Computes arbitrary-precision addition (`leftOperand + rightOperand`) on the GPU. |
| [Add(GpuBigUInt, uint, GpuBigUInt)](#addgpubiguint-uint-gpubiguint) | Adds a scalar 32-bit unsigned integer to `leftOperand` on the GPU. |
| [AddSignedMagnitude(GpuBigUInt, bool, GpuBigUInt, bool, GpuBigUInt)](#addsignedmagnitudegpubiguint-bool-gpubiguint-bool-gpubiguint) | Performs signed-magnitude addition of two signed big integers and returns the resulting sign. |
| [Sub(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#subgpubiguint-gpubiguint-gpubiguint) | Computes arbitrary-precision subtraction (`leftOperand - rightOperand`) on the GPU. |
| [Sub(GpuBigUInt, uint, GpuBigUInt)](#subgpubiguint-uint-gpubiguint) | Subtracts a scalar 32-bit unsigned integer from `leftOperand` on the GPU. |
| [Mul(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpubiguint-gpubiguint-gpubiguint) | Computes full arbitrary-precision multiplication (`leftOperand * rightOperand`) on the GPU. |
| [Mul(GpuBigUInt, uint, GpuBigUInt)](#mulgpubiguint-uint-gpubiguint) | Multiplies `leftOperand` by a scalar 32-bit unsigned integer on the GPU. |
| [Gcd(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpubiguint-gpubiguint-gpubiguint) | Computes the Greatest Common Divisor (GCD) of two big integers on the GPU. |
| [ModInverse(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modinversegpubiguint-gpubiguint-gpubiguint) | Computes the modular multiplicative inverse such that $((\text{value} \times \text{resultBuffer}) \bmod \text{modulus}) = 1$. |
| [ShiftLeft(GpuBigUInt, int, GpuBigUInt)](#shiftleftgpubiguint-int-gpubiguint) | Shifts the big integer left by the specified bit count on the GPU. |
| [ShiftRight(GpuBigUInt, int, GpuBigUInt)](#shiftrightgpubiguint-int-gpubiguint) | Shifts the big integer right by the specified bit count on the GPU. |
| [BitwiseAnd(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseandgpubiguint-gpubiguint-gpubiguint) | Performs bitwise AND on the GPU. |
| [BitwiseOr(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwiseorgpubiguint-gpubiguint-gpubiguint) | Performs bitwise OR on the GPU. |
| [BitwiseXor(GpuBigUInt, GpuBigUInt, GpuBigUInt)](#bitwisexorgpubiguint-gpubiguint-gpubiguint) | Performs bitwise XOR on the GPU. |
| [BitwiseNot(GpuBigUInt, GpuBigUInt)](#bitwisenotgpubiguint-gpubiguint) | Performs bitwise NOT on the GPU. |
| [IsLessThan(GpuBigUInt, GpuBigUInt)](#islessthangpubiguint-gpubiguint) | Determines whether `leftOperand` is strictly less than `rightOperand` on the GPU. |
| [Pow10(GpuWorkspace, int)](#pow10gpuworkspace-int) | Computes $10^{\text{exponent}}$ on the GPU using binary exponentiation by squaring. |

## Add(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision addition (`leftOperand + rightOperand`) on the GPU.

```csharp
public static void Add(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The first big integer addend.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The second big integer addend.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the sum.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## Add(GpuBigUInt, uint, GpuBigUInt)

Adds a scalar 32-bit unsigned integer to `leftOperand` on the GPU.

```csharp
public static void Add(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The big integer addend.

`rightOperand` _uint_<br>
The 32-bit unsigned scalar addend.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the sum.

## AddSignedMagnitude(GpuBigUInt, bool, GpuBigUInt, bool, GpuBigUInt)

Performs signed-magnitude addition of two signed big integers and returns the resulting sign.

```csharp
public static bool AddSignedMagnitude(
    GpuBigUInt leftOperand,
    bool leftNegative,
    GpuBigUInt rightOperand,
    bool rightNegative,
    GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The magnitude of the left operand.

`leftNegative` _bool_<br>
`true` if the left operand is negative; otherwise, `false`.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The magnitude of the right operand.

`rightNegative` _bool_<br>
`true` if the right operand is negative; otherwise, `false`.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the resulting magnitude.

#### Returns

_bool_<br>
`true` if the resulting sum is negative; otherwise, `false`.

## Sub(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes arbitrary-precision subtraction (`leftOperand - rightOperand`) on the GPU.

```csharp
public static void Sub(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The minuend operand.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The subtrahend operand.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the difference.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## Sub(GpuBigUInt, uint, GpuBigUInt)

Subtracts a scalar 32-bit unsigned integer from `leftOperand` on the GPU.

```csharp
public static void Sub(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The minuend operand.

`rightOperand` _uint_<br>
The 32-bit unsigned scalar subtrahend.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the difference.

## Mul(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes full arbitrary-precision multiplication (`leftOperand * rightOperand`) on the GPU.

```csharp
public static void Mul(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The multiplicand operand.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The multiplier operand.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the product. Must have a word capacity equal to `leftOperand.NumWords + rightOperand.NumWords`.

#### Exceptions

`ArgumentException`<br>
Thrown when `resultBuffer.NumWords` is not equal to `leftOperand.NumWords + rightOperand.NumWords`.

## Mul(GpuBigUInt, uint, GpuBigUInt)

Multiplies `leftOperand` by a scalar 32-bit unsigned integer on the GPU.

```csharp
public static void Mul(GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The multiplicand operand.

`rightOperand` _uint_<br>
The 32-bit unsigned scalar multiplier.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the product.

## Gcd(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the Greatest Common Divisor (GCD) of two big integers on the GPU using Lehmer's / Euclidean algorithm.

```csharp
public static void Gcd(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The first big integer.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The second big integer.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the GCD.

#### Exceptions

`ArgumentException`<br>
Thrown when buffer word lengths do not match.

## ModInverse(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the modular multiplicative inverse such that $((\text{value} \times \text{resultBuffer}) \bmod \text{modulus}) = 1$.

```csharp
public static void ModInverse(GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
```

#### Parameters

`value` _[GpuBigUInt](GpuBigUInt.md)_<br>
The value to invert.

`modulus` _[GpuBigUInt](GpuBigUInt.md)_<br>
The modulus coprime to `value`.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the modular inverse.

#### Exceptions

`ArgumentException`<br>
Thrown when buffer word lengths do not match.

`ArithmeticException`<br>
Thrown when the modular inverse does not exist ($\gcd(\text{value}, \text{modulus}) \neq 1$).

## ShiftLeft(GpuBigUInt, int, GpuBigUInt)

Shifts the big integer left by the specified bit count on the GPU.

```csharp
public static void ShiftLeft(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The big integer to shift.

`shiftBits` _int_<br>
The number of bit positions to shift left.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the shifted value.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## ShiftRight(GpuBigUInt, int, GpuBigUInt)

Shifts the big integer right by the specified bit count on the GPU.

```csharp
public static void ShiftRight(GpuBigUInt leftOperand, int shiftBits, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The big integer to shift.

`shiftBits` _int_<br>
The number of bit positions to shift right.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the shifted value.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## BitwiseAnd(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Performs bitwise AND on the GPU.

```csharp
public static void BitwiseAnd(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The first operand.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The second operand.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the bitwise AND result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## BitwiseOr(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Performs bitwise OR on the GPU.

```csharp
public static void BitwiseOr(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The first operand.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The second operand.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the bitwise OR result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## BitwiseXor(GpuBigUInt, GpuBigUInt, GpuBigUInt)

Performs bitwise XOR on the GPU.

```csharp
public static void BitwiseXor(GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The first operand.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The second operand.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the bitwise XOR result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand`, `rightOperand`, and `resultBuffer` do not have the same word length (`NumWords`).

## BitwiseNot(GpuBigUInt, GpuBigUInt)

Performs bitwise NOT (one's complement) on the GPU.

```csharp
public static void BitwiseNot(GpuBigUInt leftOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The operand to invert.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the bitwise NOT result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## IsLessThan(GpuBigUInt, GpuBigUInt)

Determines whether `leftOperand` is strictly less than `rightOperand` on the GPU.

```csharp
public static bool IsLessThan(GpuBigUInt leftOperand, GpuBigUInt rightOperand);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The first value to compare.

`rightOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The second value to compare.

#### Returns

_bool_<br>
`true` if `leftOperand` is strictly less than `rightOperand`; otherwise, `false`.

#### Exceptions

`ArgumentException`<br>
Thrown when buffer word lengths do not match.

## Pow10(GpuWorkspace, int)

Computes $10^{\text{exponent}}$ on the GPU using binary exponentiation by squaring.

```csharp
public static GpuBigUInt Pow10(GpuWorkspace workspace, int exponent);
```

#### Parameters

`workspace` _[GpuWorkspace](../gpu_workspace/GpuWorkspace.md)_<br>
The GPU execution workspace.

`exponent` _int_<br>
The non-negative power of 10 to compute.

#### Returns

_[GpuBigUInt](GpuBigUInt.md)_<br>
A new `GpuBigUInt` containing the result $10^{\text{exponent}}$.

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown when `exponent` is negative or exceeds `50,000,000`.

## Examples

### Basic Arithmetic Operations on the GPU

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

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
- [GpuBigUInt<TSize>](GpuUIntBigIntegerGeneric.md)
