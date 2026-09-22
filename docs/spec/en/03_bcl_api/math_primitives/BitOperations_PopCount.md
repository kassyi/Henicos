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

| Name | Description |
| --- | --- |
| [PopCount(uint)](#popcount-uint) | Returns the population count (number of bits set) of a mask. (32-bit) |
| [PopCount(ulong)](#popcount-ulong) | Returns the population count (number of bits set) of a mask. (64-bit) |

## PopCount(uint)

Returns the population count (number of bits set) of a mask. (32-bit)

```csharp
[GpuKernel]
public static int PopCount(uint value);
```

#### Parameters

`value` <i>uint</i><br>
The 32-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

## PopCount(ulong)

Returns the population count (number of bits set) of a mask. (64-bit)

```csharp
[GpuKernel]
public static int PopCount(ulong value);
```

#### Parameters

`value` <i>ulong</i><br>
The 64-bit unsigned integer.

#### Returns

<i>int</i><br>
The number of bits.

## Conceptual Behavior

Given a 32-bit unsigned integer `value`, it simply counts the number of `1` bits.

- **Input (Binary)**: `0b_0000_0000_0000_0000_0000_1111_0101_0000`
- **Output (Count)**: `6` (Because there are six `1` bits)

## Examples

### Counting Active Threads in a Warp (Ballot Mask)

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static void ProcessActiveThreads()
{
    // Evaluate a condition for all threads in the warp
    bool isActive = ThreadIndex.X % 2 == 0;
    
    // Get a 32-bit bitmask where each bit represents a thread's result
    uint ballotMask = Gpu.WarpBallotSync(0xFFFFFFFF, isActive);
    
    // Quickly count how many threads in this warp met the condition
    int activeCount = BitOperations.PopCount(ballotMask);
}
```

## Remarks

Also known as the **Hamming weight** or population count. This function returns the total number of bits set to `1` in the binary representation of the given integer. 

In GPU programming, `PopCount` is extremely common for:
- **Warp-level reductions**: Counting the total number of threads that passed a condition using a ballot mask (`Gpu.WarpBallotSync`).
- **Parity checks** and cryptography kernels.
- **Sparse array compaction**: Calculating offsets for threads to write their results into a dense array without atomic locks.

Maps natively to `__popc()` for 32-bit, and `__popcll()` for 64-bit in CUDA C++. These translate directly to the hardware `popc` PTX instructions, executing in a single clock cycle.

## See also

- [Gpu BCL API Overview](../00_index.md)
