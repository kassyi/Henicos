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

| Name                                                 | Description                                                    |
| ---------------------------------------------------- | -------------------------------------------------------------- |
| [TrailingZeroCount(uint)](#trailingzerocount-uint)   | Count the number of trailing zero bits in an integer. (32-bit) |
| [TrailingZeroCount(ulong)](#trailingzerocount-ulong) | Count the number of trailing zero bits in an integer. (64-bit) |

## TrailingZeroCount(uint)

Count the number of trailing zero bits in an integer. (32-bit)

```csharp
[GpuDevice]
public static int TrailingZeroCount(uint value);
```

#### Parameters

`value` <i>uint</i><br>
The 32-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

## TrailingZeroCount(ulong)

Count the number of trailing zero bits in an integer. (64-bit)

```csharp
[GpuDevice]
public static int TrailingZeroCount(ulong value);
```

#### Parameters

`value` <i>ulong</i><br>
The 64-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

## Conceptual Behavior

Given a 32-bit unsigned integer `value`, it counts consecutive `0` bits starting from the **right** (Least Significant Bit).

- **Input (Binary)**: `0b_0000_0000_0000_0000_0000_1111_0101_0000`
- **Output (Count)**: `4` (There are 4 consecutive `0`s at the end before the first `1` appears)

## Examples

### Finding the First Set Bit (Index)

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void FindFirstActiveTask()
{
    // Example: A bitmask representing active tasks
    uint taskMask = 0b_0000_0000_0000_0000_0000_0000_0001_0000;

    if (taskMask != 0)
    {
        // Find the index of the first active task (which is 4)
        int firstTaskIndex = BitOperations.TrailingZeroCount(taskMask);

        if (ThreadIndex.X == firstTaskIndex)
        {
            // Only the thread matching the first active task index executes this block
        }
    }
}
```

## Remarks

Counts the number of consecutive `0` bits starting from the Least Significant Bit (LSB) up to the Most Significant Bit (MSB). If the value is `0`, it returns `32` (for `uint`) or `64` (for `ulong`).

In GPU programming, `TrailingZeroCount` is a critical primitive for:

- **Warp Leader Election**: When combined with a ballot mask (`Gpu.WarpBallotSync`), it returns the exact warp lane ID (0-31) of the lowest-numbered thread that voted true.
- **Bitmask Iteration**: Quickly jumping to the next set bit in a mask without checking every bit in a loop.
- **Divisibility Checks**: Determining the largest power of 2 that divides a number.

Maps natively to `__gsharp_tzc_32()` for 32-bit, and `__gsharp_tzc_64()` for 64-bit in CUDA C++ (which wrap the `__ffs` intrinsic).

## See also

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
