---
title: "GpuBigIntScalarKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for large integer operations involving scalar 32-bit operands."
---

## GpuBigIntScalarKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels used for arithmetic operations between arbitrary-precision integers and scalar 32-bit values.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntScalarKernels
```

## Methods

| Name | Description |
| --- | --- |
| [ScalarAddKernel(uint[], uint, uint[], int)](#scalaraddkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs scalar addition on a large integer. |
| [ScalarSubKernel(uint[], uint, uint[], int)](#scalarsubkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs scalar subtraction on a large integer. |
| [ScalarMulKernel(uint[], uint, uint[], int)](#scalarmulkerneluint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs scalar multiplication on a large integer. |
| [ChunkScalarMulKernel(uint[], uint, uint[], long[], int, int)](#chunkscalarmulkerneluint-uint-uint-long-int-int) | A GPU kernel (`[GpuKernel]`) that performs scalar multiplication on a large integer in chunks. |

## ScalarAddKernel(uint[], uint, uint[], int)

A GPU kernel (`[GpuKernel]`) that performs scalar addition on a large integer.

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

A GPU kernel (`[GpuKernel]`) that performs scalar subtraction on a large integer.

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

## ScalarMulKernel(uint[], uint, uint[], int)

A GPU kernel (`[GpuKernel]`) that performs scalar multiplication on a large integer.

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

## ChunkScalarMulKernel(uint[], uint, uint[], long[], int, int)

A GPU kernel (`[GpuKernel]`) that performs scalar multiplication on a large integer in chunks.

```csharp
[GpuKernel]
public static void ChunkScalarMulKernel(uint[] dA, uint s, uint[] dResult, long[] dCarry, int numWords, int chunkSize);
```

#### Parameters

`dA` _uint[]_<br>
The large integer operand buffer.

`s` _uint_<br>
The scalar 32-bit unsigned integer to multiply by.

`dResult` _uint[]_<br>
The buffer to store the result.

`dCarry` _long[]_<br>
The buffer to store chunk carries.

`numWords` _int_<br>
The total number of words.

`chunkSize` _int_<br>
The number of words processed per block.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntScalarKernels contains static kernel endpoints for scalar ops.");
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
