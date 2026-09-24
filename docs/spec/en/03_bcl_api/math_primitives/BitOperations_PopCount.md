---
title: "BitOperations.PopCount Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the population count (number of bits set) of a mask."
---

## BitOperations.PopCount Method

## Definition

**Namespace:** `System.Numerics`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.

Returns the population count (number of bits set) of a mask.

## Overloads

| Name                               | Description                                                           |
| ---------------------------------- | --------------------------------------------------------------------- |
| [PopCount(uint)](#popcount-uint)   | Returns the population count (number of bits set) of a mask. (32-bit) |
| [PopCount(ulong)](#popcount-ulong) | Returns the population count (number of bits set) of a mask. (64-bit) |

## PopCount(uint)

Returns the population count (number of bits set) of a mask. (32-bit)

```csharp
[GpuDevice]
public static int PopCount(uint value);
```

#### Parameters

`value` <i>uint</i><br>
The 32-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `0` if `value` is `0`.

## PopCount(ulong)

Returns the population count (number of bits set) of a mask. (64-bit)

```csharp
[GpuDevice]
public static int PopCount(ulong value);
```

#### Parameters

`value` <i>ulong</i><br>
The 64-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

#### Exceptions

None. Returns `0` if `value` is `0`.

## Conceptual Behavior

Given a 32-bit unsigned integer `value`, it simply counts the number of `1` bits.

- **Input (Binary)**: `0b_0000_0000_0000_0000_0000_1111_0101_0000`
- **Output (Count)**: `6` (Because there are six `1` bits)

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using System.Numerics;

// Count the number of set bits (1s)
uint value = 0b1011; // 11 in decimal
int setBits = BitOperations.PopCount(value);
Console.WriteLine($"PopCount(0b1011) = {setBits}");
```
## See also

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
