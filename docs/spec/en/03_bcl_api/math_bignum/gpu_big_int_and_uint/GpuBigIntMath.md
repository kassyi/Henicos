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

Operands and result buffers are [`GpuBigUIntView`](GpuBigUIntView.md)s. A `GpuBigUInt` converts to a view implicitly, so owned values and element views (`array.GetElement(i)`) can be passed alike; the operation borrows them for the duration of the call.

Inheritance: `Object` $\rightarrow$ `GpuBigIntMath`

## Methods

| Name | Description |
| --- | --- |
| [Add(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#addgpubiguintview-gpubiguintview-gpubiguintview) | Computes arbitrary-precision addition (`leftOperand + rightOperand`) on the GPU. |
| [Add(GpuBigUIntView, uint, GpuBigUIntView)](#addgpubiguintview-uint-gpubiguintview) | Adds a scalar 32-bit unsigned integer to `leftOperand` on the GPU. |
| [AddSignedMagnitude(GpuBigUIntView, bool, GpuBigUIntView, bool, GpuBigUIntView)](#addsignedmagnitudegpubiguintview-bool-gpubiguintview-bool-gpubiguintview) | Performs signed-magnitude addition of two signed big integers and returns the resulting sign. |
| [Sub(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#subgpubiguintview-gpubiguintview-gpubiguintview) | Computes arbitrary-precision subtraction (`leftOperand - rightOperand`) on the GPU. |
| [Sub(GpuBigUIntView, uint, GpuBigUIntView)](#subgpubiguintview-uint-gpubiguintview) | Subtracts a scalar 32-bit unsigned integer from `leftOperand` on the GPU. |
| [Mul(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#mulgpubiguintview-gpubiguintview-gpubiguintview) | Computes full arbitrary-precision multiplication (`leftOperand * rightOperand`) on the GPU. |
| [Mul(GpuBigUIntView, uint, GpuBigUIntView)](#mulgpubiguintview-uint-gpubiguintview) | Multiplies `leftOperand` by a scalar 32-bit unsigned integer on the GPU. |
| [Gcd(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#gcdgpubiguintview-gpubiguintview-gpubiguintview) | Computes the Greatest Common Divisor (GCD) of two big integers on the GPU. |
| [ModInverse(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#modinversegpubiguintview-gpubiguintview-gpubiguintview) | Computes the modular multiplicative inverse such that $((\text{value} \times \text{resultBuffer}) \bmod \text{modulus}) = 1$. |
| [ShiftLeft(GpuBigUIntView, int, GpuBigUIntView)](#shiftleftgpubiguintview-int-gpubiguintview) | Shifts the big integer left by the specified bit count on the GPU. |
| [ShiftRight(GpuBigUIntView, int, GpuBigUIntView)](#shiftrightgpubiguintview-int-gpubiguintview) | Shifts the big integer right by the specified bit count on the GPU. |
| [BitwiseAnd(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#bitwiseandgpubiguintview-gpubiguintview-gpubiguintview) | Performs bitwise AND on the GPU. |
| [BitwiseOr(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#bitwiseorgpubiguintview-gpubiguintview-gpubiguintview) | Performs bitwise OR on the GPU. |
| [BitwiseXor(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)](#bitwisexorgpubiguintview-gpubiguintview-gpubiguintview) | Performs bitwise XOR on the GPU. |
| [BitwiseNot(GpuBigUIntView, GpuBigUIntView)](#bitwisenotgpubiguintview-gpubiguintview) | Performs bitwise NOT on the GPU. |
| [IsLessThan(GpuBigUIntView, GpuBigUIntView)](#islessthangpubiguintview-gpubiguintview) | Determines whether `leftOperand` is strictly less than `rightOperand` on the GPU. |
| [Pow10(GpuWorkspace, int)](#pow10gpuworkspace-int) | Computes $10^{\text{exponent}}$ on the GPU using binary exponentiation by squaring. |

## Add(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Computes arbitrary-precision addition (`leftOperand + rightOperand`) on the GPU.

```csharp
public static void Add(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
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

## Add(GpuBigUIntView, uint, GpuBigUIntView)

Adds a scalar 32-bit unsigned integer to `leftOperand` on the GPU.

```csharp
public static void Add(scoped GpuBigUIntView leftOperand, uint rightOperand, scoped GpuBigUIntView resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The big integer addend.

`rightOperand` _uint_<br>
The 32-bit unsigned scalar addend.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the sum.

## AddSignedMagnitude(GpuBigUIntView, bool, GpuBigUIntView, bool, GpuBigUIntView)

Performs signed-magnitude addition of two signed big integers and returns the resulting sign.

```csharp
public static bool AddSignedMagnitude(
    scoped GpuBigUIntView leftOperand,
    bool leftNegative,
    scoped GpuBigUIntView rightOperand,
    bool rightNegative,
    scoped GpuBigUIntView resultBuffer);
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

## Sub(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Computes arbitrary-precision subtraction (`leftOperand - rightOperand`) on the GPU.

```csharp
public static void Sub(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
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

## Sub(GpuBigUIntView, uint, GpuBigUIntView)

Subtracts a scalar 32-bit unsigned integer from `leftOperand` on the GPU.

```csharp
public static void Sub(scoped GpuBigUIntView leftOperand, uint rightOperand, scoped GpuBigUIntView resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The minuend operand.

`rightOperand` _uint_<br>
The 32-bit unsigned scalar subtrahend.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the difference.

## Mul(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Computes full arbitrary-precision multiplication (`leftOperand * rightOperand`) on the GPU.

```csharp
public static void Mul(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
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

## Mul(GpuBigUIntView, uint, GpuBigUIntView)

Multiplies `leftOperand` by a scalar 32-bit unsigned integer on the GPU.

```csharp
public static void Mul(scoped GpuBigUIntView leftOperand, uint rightOperand, scoped GpuBigUIntView resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The multiplicand operand.

`rightOperand` _uint_<br>
The 32-bit unsigned scalar multiplier.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer in GPU device memory receiving the product.

## Gcd(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Computes the Greatest Common Divisor (GCD) of two big integers on the GPU using Lehmer's / Euclidean algorithm.

```csharp
public static void Gcd(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
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

## ModInverse(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Computes the modular multiplicative inverse such that $((\text{value} \times \text{resultBuffer}) \bmod \text{modulus}) = 1$.

```csharp
public static void ModInverse(scoped GpuBigUIntView value, scoped GpuBigUIntView modulus, scoped GpuBigUIntView resultBuffer);
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

## ShiftLeft(GpuBigUIntView, int, GpuBigUIntView)

Shifts the big integer left by the specified bit count on the GPU.

```csharp
public static void ShiftLeft(scoped GpuBigUIntView leftOperand, int shiftBits, scoped GpuBigUIntView resultBuffer);
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

## ShiftRight(GpuBigUIntView, int, GpuBigUIntView)

Shifts the big integer right by the specified bit count on the GPU.

```csharp
public static void ShiftRight(scoped GpuBigUIntView leftOperand, int shiftBits, scoped GpuBigUIntView resultBuffer);
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

## BitwiseAnd(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Performs bitwise AND on the GPU.

```csharp
public static void BitwiseAnd(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
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

## BitwiseOr(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Performs bitwise OR on the GPU.

```csharp
public static void BitwiseOr(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
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

## BitwiseXor(GpuBigUIntView, GpuBigUIntView, GpuBigUIntView)

Performs bitwise XOR on the GPU.

```csharp
public static void BitwiseXor(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand, scoped GpuBigUIntView resultBuffer);
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

## BitwiseNot(GpuBigUIntView, GpuBigUIntView)

Performs bitwise NOT (one's complement) on the GPU.

```csharp
public static void BitwiseNot(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView resultBuffer);
```

#### Parameters

`leftOperand` _[GpuBigUInt](GpuBigUInt.md)_<br>
The operand to invert.

`resultBuffer` _[GpuBigUInt](GpuBigUInt.md)_<br>
The destination buffer receiving the bitwise NOT result.

#### Exceptions

`ArgumentException`<br>
Thrown when `leftOperand` and `resultBuffer` do not have the same word length (`NumWords`).

## IsLessThan(GpuBigUIntView, GpuBigUIntView)

Determines whether `leftOperand` is strictly less than `rightOperand` on the GPU.

```csharp
public static bool IsLessThan(scoped GpuBigUIntView leftOperand, scoped GpuBigUIntView rightOperand);
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
