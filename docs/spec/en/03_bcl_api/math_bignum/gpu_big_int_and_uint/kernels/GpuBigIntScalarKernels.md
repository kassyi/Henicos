---
title: "GpuBigIntScalarKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for large integer operations involving scalar 32-bit operands."
---

## GpuBigIntScalarKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

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
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntScalarKernels contains static kernel endpoints for scalar ops.");
```

## Remarks

This class contains `[GpuKernel]` and `[GpuDeviceFunction]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuContext`.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
