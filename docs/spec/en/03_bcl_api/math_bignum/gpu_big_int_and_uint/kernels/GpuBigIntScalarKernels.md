---
title: "GpuBigIntScalarKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for large integer operations involving scalar 32-bit operands."
---

## GpuBigIntScalarKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and device functions used for parallel arithmetic operations between arbitrary-precision integers and scalar 32-bit unsigned values.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntScalarKernels
```

## Methods

| Name | Description |
| --- | --- |
| [ScalarAddKernel(uint[], uint, uint[], int, int, uint[])](#scalaraddkerneluint-uint-uint-int-int-uint) | A GPU kernel (`[GpuKernel]`) that computes `result = a + s`. |
| [ScalarSubKernel(uint[], uint, uint[], int, int, uint[])](#scalarsubkerneluint-uint-uint-int-int-uint) | A GPU kernel (`[GpuKernel]`) that computes `result = a - s`. |
| [DeviceScalarAddSub(uint[], uint, uint[], int, int, int, uint[], uint)](#devicescalaraddsubuint-uint-uint-int-int-int-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that adds or subtracts a scalar in one block with a carry lookahead scan. |
| [ScalarMulKernel(uint[], uint, uint[], int, int, uint[])](#scalarmulkerneluint-uint-uint-int-int-uint) | A GPU kernel (`[GpuKernel]`) that computes `result = a * s`. |
| [DeviceScalarMul(uint[], uint, uint[], int, int, uint[])](#devicescalarmuluint-uint-uint-int-int-uint) | A GPU device function (`[GpuDeviceFunction]`) that multiplies by a scalar in one block with warp shuffles and a carry lookahead scan. |
| [ScalarDivByUInt32Kernel(uint[], uint, uint[], uint[], int)](#scalardivbyuint32kerneluint-uint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that divides by a 32-bit scalar and writes the quotient and the remainder. |

The add, subtract and multiply kernels take the result width `resultWords` and the workspace's device status words `status`. A result that does not fit is a domain error: the kernel records it in `status` (`GpuDomainErrors`), and the host reports it as `OverflowException` at the next read of the workspace stream. Launch each of them with one block whose size is a multiple of 32.

## ScalarAddKernel(uint[], uint, uint[], int, int, uint[])

A GPU kernel (`[GpuKernel]`) that computes `result = a + s`. A result wider than `numWords` receives the carry in word `numWords` and zeros above it; a result of `numWords` words reports a carry as `GpuDomainErrors.ScalarAddOverflow`.

```csharp
[GpuKernel]
public static void ScalarAddKernel(uint[] dA, uint s, uint[] dResult, int numWords, int resultWords, uint[] status);
```

#### Parameters

`dA` _uint[]_<br>
The large integer operand buffer.

`s` _uint_<br>
The scalar 32-bit unsigned integer to add.

`dResult` _uint[]_<br>
The buffer to store the result, `resultWords` words.

`numWords` _int_<br>
The word count of `dA`.

`resultWords` _int_<br>
The word count of `dResult`, at least `numWords`.

`status` _uint[]_<br>
The workspace's device status words, which receive a domain error.

## ScalarSubKernel(uint[], uint, uint[], int, int, uint[])

A GPU kernel (`[GpuKernel]`) that computes `result = a - s`. A result wider than `numWords` receives zeros above the difference; a borrow (`a < s`) is reported as `GpuDomainErrors.ScalarSubtractBelowZero` at any width. The parameters are those of `ScalarAddKernel`.

```csharp
[GpuKernel]
public static void ScalarSubKernel(uint[] dA, uint s, uint[] dResult, int numWords, int resultWords, uint[] status);
```

## DeviceScalarAddSub(uint[], uint, uint[], int, int, int, uint[], uint)

A GPU device function (`[GpuDeviceFunction]`) that computes `a + s` (`subtract = 0`) or `a - s` (`subtract = 1`) over `numWords` words, then the word above them, and reports `code` to `status` when the result does not fit. `a` may be the result itself (`x++`): each thread reads its word before it writes it.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceScalarAddSub(
    uint[] a,
    uint s,
    uint[] result,
    int numWords,
    int resultWords,
    int subtract,
    uint[] status,
    uint code);
```

## ScalarMulKernel(uint[], uint, uint[], int, int, uint[])

A GPU kernel (`[GpuKernel]`) that computes `result = a * s`. A result wider than `numWords` receives the top word of the product in word `numWords` and zeros above it; a result of `numWords` words reports a non-zero top word as `GpuDomainErrors.ScalarMultiplyOverflow`. The parameters are those of `ScalarAddKernel`.

```csharp
[GpuKernel]
public static void ScalarMulKernel(uint[] dA, uint s, uint[] dResult, int numWords, int resultWords, uint[] status);
```

## DeviceScalarMul(uint[], uint, uint[], int, int, uint[])

A GPU device function (`[GpuDeviceFunction]`) that computes `a * s` over `numWords` words, then the top word, in one block. `a` may be the result itself.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceScalarMul(
    uint[] a,
    uint s,
    uint[] result,
    int numWords,
    int resultWords,
    uint[] status);
```

## ScalarDivByUInt32Kernel(uint[], uint, uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that divides `dA` by `divisor` from the top word down on one thread (thread 0 of block 0), writing the quotient to `dResult` and the remainder to `dRemainder[0]`.

```csharp
[GpuKernel]
public static void ScalarDivByUInt32Kernel(
    [GpuReadOnly] uint[] dA,
    uint divisor,
    uint[] dResult,
    uint[] dRemainder,
    int numWords);
```

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// This is a static kernel container class whose endpoints are launched via the G# execution pipeline.
Console.WriteLine($"{nameof(GpuBigIntScalarKernels)} provides parallel scalar add, sub, and mul kernels.");
```

## Remarks

This class contains `[GpuKernel]` and `[GpuDeviceFunction]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuWorkspace`.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### BCL API > GPU Class

- [Gpu Class](../../../../03_bcl_api/gpu_class/00_index.md)
- [InlinePtx](../../../../03_bcl_api/gpu_class/InlinePtx.md)

### Guides

- [4. Practical Guides and Best Practices](../../../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
- [7. Stdlib Math and Lazy Evaluation](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
