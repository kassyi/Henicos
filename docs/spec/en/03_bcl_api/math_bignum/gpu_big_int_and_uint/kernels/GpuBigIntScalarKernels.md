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
| [ScalarAddKernel(uint[], uint, uint[], int)](#scalaraddkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs parallel scalar addition on a large integer. |
| [ScalarSubKernel(uint[], uint, uint[], int)](#scalarsubkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs parallel scalar subtraction on a large integer. |
| [Device_ScalarAddSubParallel(uint[], uint, uint[], int, int, bool)](#device_scalaraddsubparalleluint-uint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel scalar addition or subtraction using Kogge-Stone carry lookahead. |
| [ScalarMulKernel(uint[], uint, uint[], int)](#scalarmulkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs parallel scalar multiplication on a large integer. |
| [DeviceScalarMulParallel(uint[], uint, uint[], int, bool)](#devicescalarmulparalleluint-uint-uint-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel scalar multiplication using warp shuffle and Kogge-Stone carry lookahead. |

## ScalarAddKernel(uint[], uint, uint[], int)

A GPU kernel (`[GpuKernel]`) that performs parallel scalar addition on a large integer.

```csharp
[GpuKernel]
public static void ScalarAddKernel(uint[] dA, uint s, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The large integer operand buffer.

`s` _uint_<br>
The scalar 32-bit unsigned integer to add.

`dResult` _uint[]_<br>
The buffer to store the result.

`numWords` _int_<br>
The total number of words.

## ScalarSubKernel(uint[], uint, uint[], int)

A GPU kernel (`[GpuKernel]`) that performs parallel scalar subtraction on a large integer.

```csharp
[GpuKernel]
public static void ScalarSubKernel(uint[] dA, uint s, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The large integer operand buffer.

`s` _uint_<br>
The scalar 32-bit unsigned integer to subtract.

`dResult` _uint[]_<br>
The buffer to store the result.

`numWords` _int_<br>
The total number of words.

## Device_ScalarAddSubParallel(uint[], uint, uint[], int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel scalar addition or subtraction using Kogge-Stone carry lookahead.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void Device_ScalarAddSubParallel(
    [GpuReadOnly] uint[] a,
    uint s,
    uint[] result,
    int numWords,
    int sign,
    bool syncThreads = true);
```

#### Parameters

`a` _uint[]_<br>
The large integer operand buffer.

`s` _uint_<br>
The scalar 32-bit unsigned integer operand.

`result` _uint[]_<br>
The destination buffer.

`numWords` _int_<br>
The total number of words.

`sign` _int_<br>
`1` for scalar addition (`a + s`), or `-1` for scalar subtraction (`a - s`).

`syncThreads` _bool_<br>
If `true`, synchronizes threads within the block upon completion.

## ScalarMulKernel(uint[], uint, uint[], int)

A GPU kernel (`[GpuKernel]`) that performs parallel scalar multiplication on a large integer.

```csharp
[GpuKernel]
public static void ScalarMulKernel(uint[] dA, uint s, uint[] dResult, int numWords);
```

#### Parameters

`dA` _uint[]_<br>
The large integer operand buffer.

`s` _uint_<br>
The scalar 32-bit unsigned integer to multiply by.

`dResult` _uint[]_<br>
The buffer to store the result.

`numWords` _int_<br>
The total number of words.

## DeviceScalarMulParallel(uint[], uint, uint[], int, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs block-parallel scalar multiplication using warp shuffle and Kogge-Stone carry lookahead.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceScalarMulParallel(
    [GpuReadOnly] uint[] a,
    uint s,
    uint[] result,
    int numWords,
    bool syncThreads = true);
```

#### Parameters

`a` _uint[]_<br>
The large integer operand buffer.

`s` _uint_<br>
The scalar 32-bit unsigned integer multiplier.

`result` _uint[]_<br>
The destination buffer.

`numWords` _int_<br>
The total number of words.

`syncThreads` _bool_<br>
If `true`, synchronizes threads within the block upon completion.

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
