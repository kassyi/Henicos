---
title: "BitOperations.TrailingZeroCount Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Count the number of trailing zero bits in an integer."
---

## BitOperations.TrailingZeroCount Method

## Definition

**Namespace:** `System.Numerics`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.

Count the number of trailing zero bits in an integer.

## Overloads

| Name | Description |
| --- | --- |
| [TrailingZeroCount(int)](#trailingzerocount-int) | Count the number of trailing zero bits in an integer. (32-bit signed) |
| [TrailingZeroCount(uint)](#trailingzerocount-uint) | Count the number of trailing zero bits in an integer. (32-bit) |
| [TrailingZeroCount(long)](#trailingzerocount-long) | Count the number of trailing zero bits in an integer. (64-bit signed) |
| [TrailingZeroCount(nint)](#trailingzerocount-nint) | Count the number of trailing zero bits in an integer. (pointer-sized signed) |
| [TrailingZeroCount(ulong)](#trailingzerocount-ulong) | Count the number of trailing zero bits in an integer. (64-bit) |
| [TrailingZeroCount(nuint)](#trailingzerocount-nuint) | Count the number of trailing zero bits in an integer. (pointer-sized) |

<a id="trailingzerocount-int"></a>

## TrailingZeroCount(int)

Count the number of trailing zero bits in an integer. (32-bit signed)

```csharp
public static int TrailingZeroCount(int value);
```

#### Parameters

`value` <i>int</i><br>
The 32-bit signed integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `32` if `value` is `0`.

## TrailingZeroCount(uint)

Count the number of trailing zero bits in an integer. (32-bit)

```csharp
public static int TrailingZeroCount(uint value);
```

#### Parameters

`value` <i>uint</i><br>
The 32-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `32` if `value` is `0`.

## TrailingZeroCount(ulong)

Count the number of trailing zero bits in an integer. (64-bit)

```csharp
public static int TrailingZeroCount(ulong value);
```

#### Parameters

`value` <i>ulong</i><br>
The 64-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `64` if `value` is `0`.

<a id="trailingzerocount-nuint"></a>

## TrailingZeroCount(nuint)

Count the number of trailing zero bits in an integer. (pointer-sized)

```csharp
public static int TrailingZeroCount(nuint value);
```

#### Parameters

`value` <i>nuint</i><br>
The pointer-sized unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `64` if `value` is `0`.

<a id="trailingzerocount-long"></a>

## TrailingZeroCount(long)

Count the number of trailing zero bits in an integer. (64-bit signed)

```csharp
public static int TrailingZeroCount(long value);
```

#### Parameters

`value` <i>long</i><br>
The 64-bit signed integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `64` if `value` is `0`.

<a id="trailingzerocount-nint"></a>

## TrailingZeroCount(nint)

Count the number of trailing zero bits in an integer. (pointer-sized signed)

```csharp
public static int TrailingZeroCount(nint value);
```

#### Parameters

`value` <i>nint</i><br>
The pointer-sized signed integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `64` if `value` is `0`.

## Conceptual Behavior

Given a 32-bit unsigned integer `value`, it counts consecutive `0` bits starting from the **right** (Least Significant Bit).

- **Input (Binary)**: `0b_0000_0000_0000_0000_0000_1111_0101_0000`
- **Output (Count)**: `4` (There are 4 consecutive `0`s at the end before the first `1` appears)

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using System.Numerics;

// Count the number of trailing zero bits
uint value = 0b1011000; // 88 in decimal
int trailingZeros = BitOperations.TrailingZeroCount(value);
Console.WriteLine($"TrailingZeroCount(0b1011000) = {trailingZeros}");
```
## See also

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
