---
title: "BitOperations.LeadingZeroCount Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Count the number of leading zero bits in an integer."
---

## BitOperations.LeadingZeroCount Method

## Definition

**Namespace:** `System.Numerics`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Count the number of leading zero bits in an integer.

## Overloads

| Name | Description |
| --- | --- |
| [LeadingZeroCount(uint)](#leadingzerocount-uint) | Count the number of leading zero bits in an integer. (32-bit) |
| [LeadingZeroCount(ulong)](#leadingzerocount-ulong) | Count the number of leading zero bits in an integer. (64-bit) |

## LeadingZeroCount(uint)

Count the number of leading zero bits in an integer. (32-bit)

```csharp
[GpuDevice]
public static int LeadingZeroCount(uint value);
```

#### Parameters

`value` <i>uint</i><br>
The 32-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `32` if `value` is `0`.

## LeadingZeroCount(ulong)

Count the number of leading zero bits in an integer. (64-bit)

```csharp
[GpuDevice]
public static int LeadingZeroCount(ulong value);
```

#### Parameters

`value` <i>ulong</i><br>
The 64-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `64` if `value` is `0`.

## Conceptual Behavior

Given a 32-bit unsigned integer `value`, it counts consecutive `0` bits starting from the **left** (Most Significant Bit).

- **Input (Binary)**: `0b_0000_0000_0000_0000_0000_1111_0101_0000`
- **Output (Count)**: `20` (There are 20 consecutive `0`s before the first `1` appears)

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using System.Numerics;

// Count the number of leading zero bits
uint value = 0b1011; // 11 in decimal (32-bit integer)
int leadingZeros = BitOperations.LeadingZeroCount(value);
// 32 - 4 = 28 leading zeros
Console.WriteLine($"LeadingZeroCount(0b1011) = {leadingZeros}");
```
## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)

