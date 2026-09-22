---
title: "2. Arbitrary-Precision GPU Floating-Point Module (GpuBigFloat / GpuBigUFloat)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "The `Kassyi.GSharp.Stdlib.Math.BigFloat` namespace provides high-performance, arbitrary-precision floating-point arithmetic executed natively on NV..."
---

# 2. Arbitrary-Precision GPU Floating-Point Module (GpuBigFloat / GpuBigUFloat)

The `Kassyi.GSharp.Stdlib.Math.BigFloat` namespace provides high-performance, arbitrary-precision floating-point arithmetic executed natively on NVIDIA GPUs. It allows you to work with extremely large floating-point numbers without triggering garbage collection pauses.

---

## Part A: API Reference & Usage

To provide an intuitive, zero-allocation developer experience natively in C#, the module leverages C# 12 `[InlineArray]` and `[CollectionBuilder]` to define fixed-size wrappers around generic memory layouts.

### `GpuFloat128`, `GpuFloat256`, `GpuFloat512` Structs

Concrete unmanaged wrapper structs for signed arbitrary-precision floating-point numbers.

**Syntax**
```csharp
// Execution Context: Host (CPU) & G# Kernel (GPU)
public struct GpuFloat128
{
    public GpuFloat128(GpuUInt128 significand, int exponent, bool isNegative) { ... }
}
```

**Example 1: Initialization on the Host (CPU)**
```csharp
// Execution Context: Host (CPU)
using Kassyi.GSharp.Stdlib.Math.BigFloat;

// 1. Initialize the significand (mantissa) using Collection Expressions
// For a 128-bit float, we provide 4 x 32-bit uint chunks.
GpuUInt128 significand = [0xFFFFFFFF, 0x00000000, 0x00000000, 0x00000000];

// 2. Construct the float
var myFloat = new GpuFloat128(significand, exponent: 10, isNegative: false);
```

**Example 2: Arithmetic Operations in the Kernel (GPU)**
GpuBigFloat types support standard operator overloading for intuitive arithmetic directly inside GPU kernels.

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static unsafe void BigFloatAddKernel(GpuFloat128* a, GpuFloat128* b, GpuFloat128* result)
{
    int tid = ThreadIndex.X;
    // Standard addition operator resolves to the optimized GPU math implementation
    result[tid] = a[tid] + b[tid];
}
```

### `GpuBigFloatRoundingMode` Enum

Specifies the IEEE 754 compliant rounding mode to use during mathematical operations.

**Syntax**
```csharp
// Execution Context: Host (CPU) & G# Kernel (GPU)
public enum GpuBigFloatRoundingMode
{
    RoundToNearestEven = 0,
    RoundTowardZero = 1,
    RoundTowardPositiveInfinity = 2,
    RoundTowardNegativeInfinity = 3
}
```

---

## Part B: Architecture and Implementation Details (Advanced)

For deep internal architecture details regarding IEEE 754 bit-alignment, NTT multiplication, and memory safety invariants, refer to:
**[BigNum Internals](../02_architecture/04_bignum_internals.md)**