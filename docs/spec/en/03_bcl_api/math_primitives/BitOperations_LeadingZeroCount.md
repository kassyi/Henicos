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

## Conceptual Behavior

Given a 32-bit unsigned integer `value`, it counts consecutive `0` bits starting from the **left** (Most Significant Bit).

- **Input (Binary)**: `0b_0000_0000_0000_0000_0000_1111_0101_0000`
- **Output (Count)**: `20` (There are 20 consecutive `0`s before the first `1` appears)

## Examples

### Fast Base-2 Logarithm (Highest Set Bit)

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void FastLog2Example()
{
    uint value = 0b_0000_1000_0000_0000u; // 2048
    
    // Count leading zeros (from MSB to LSB)
    int lz = BitOperations.LeadingZeroCount(value); // Returns 20 (for 32-bit)
    
    // Find the index of the highest set bit (floor(log2(x)))
    int highestBit = 31 - lz; // Returns 11
}
```

## Remarks

Counts the number of consecutive `0` bits starting from the Most Significant Bit (MSB) down to the Least Significant Bit (LSB). If the value is `0`, it returns `32` (for `uint`) or `64` (for `ulong`).

In GPU programming, `LeadingZeroCount` is frequently used for:
- **Fast Base-2 Logarithms**: Finding `floor(log2(x))` instantly without using floating-point math units (`31 - LeadingZeroCount(x)`).
- **Radix Trees / Prefix Sums**: Quickly finding the most significant differing bit between two hash values or Morton codes.
- **Memory Allocators**: Finding the appropriately sized bucket/bin for a given dynamic allocation size.

Maps natively to `__gsharp_lzc_32()` for 32-bit, and `__gsharp_lzc_64()` for 64-bit in CUDA C++ (which wrap the `__clz` / `__clzll` intrinsics). These map directly to the hardware `clz` PTX instructions.

## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)

