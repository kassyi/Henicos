---
title: "GpuBigIntMulKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for large integer multiplication using Number Theoretic Transform (NTT)."
---

## GpuBigIntMulKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Contains the native G# (CUDA) kernels used for multi-million digit multiplications using a Global Non-Cooperative NTT approach on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static class GpuBigIntMulKernels
```

## Methods

| Name | Description |
| --- | --- |
| [Kernel_MultiplyNtt_Unpack(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int)](#kernel_multiplyntt_unpackuint-uint-uint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that unpacks the operands into NTT buffers. |
| [Kernel_MultiplyNtt_SharedRow(uint[], uint[], uint[], int, int, int, uint, ulong, bool, bool, int, int)](#kernel_multiplyntt_sharedrowuint-uint-uint-int-int-int-uint-ulong-bool-bool-int-int) | A GPU kernel (`[GpuKernel]`) that performs shared row-wise NTT transformations. |
| [Kernel_MultiplyNtt_Transpose(uint[], uint[], int, int)](#kernel_multiplyntt_transposeuint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that transposes the NTT matrix. |
| [Kernel_MultiplyNtt_PointwiseMultiply(uint[], uint[], uint[], uint[], int)](#kernel_multiplyntt_pointwisemultiplyuint-uint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs pointwise modular multiplication in the frequency domain. |
| [Kernel_MultiplyNtt_ComputeCRT(uint[], uint[], uint[], uint[], int)](#kernel_multiplyntt_computecrtuint-uint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that computes the Chinese Remainder Theorem (CRT) coefficients to recombine NTT results. |

## Kernel_MultiplyNtt_Unpack(uint[], uint[], uint[], uint[], uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that unpacks the operands into NTT buffers.

```csharp
[GpuKernel]
public static void Kernel_MultiplyNtt_Unpack(
    uint[] opA, uint[] opB, uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B,
    int transformLength, int numWordsA, int numWordsB);
```

#### Parameters

`opA` _uint[]_<br>
The first operand buffer.

`opB` _uint[]_<br>
The second operand buffer.

`nttBuf1A` _uint[]_<br>
The destination buffer for operand A corresponding to the first prime modulus.

`nttBuf2A` _uint[]_<br>
The destination buffer for operand A corresponding to the second prime modulus.

`nttBuf1B` _uint[]_<br>
The destination buffer for operand B corresponding to the first prime modulus.

`nttBuf2B` _uint[]_<br>
The destination buffer for operand B corresponding to the second prime modulus.

`transformLength` _int_<br>
The length of the NTT transform.

`numWordsA` _int_<br>
The word count of operand A.

`numWordsB` _int_<br>
The word count of operand B.

## Kernel_MultiplyNtt_SharedRow(uint[], uint[], uint[], int, int, int, uint, ulong, bool, bool, int, int)

A GPU kernel (`[GpuKernel]`) that performs shared row-wise NTT transformations.

```csharp
[GpuKernel]
public static void Kernel_MultiplyNtt_SharedRow(
    uint[] buffer, uint[] temporaryBuffer, uint[] twiddle,
    int m, int n1, int n2, uint p, ulong pm,
    bool applyTwiddleBefore, bool applyTwiddleAfter,
    int maxBlocks, int currentL);
```

#### Parameters

`buffer` _uint[]_<br>
The primary input/output data buffer.

`temporaryBuffer` _uint[]_<br>
The temporary buffer used during transposition.

`twiddle` _uint[]_<br>
The twiddle factor array.

`m` _int_<br>
The transform length.

`n1` _int_<br>
The first dimension size.

`n2` _int_<br>
The second dimension size.

`p` _uint_<br>
The prime modulus for the NTT.

`pm` _ulong_<br>
The Barrett reduction magic number.

`applyTwiddleBefore` _bool_<br>
If `true`, applies twiddle factors before the butterfly operation.

`applyTwiddleAfter` _bool_<br>
If `true`, applies twiddle factors after the butterfly operation.

`maxBlocks` _int_<br>
The maximum number of blocks to process.

`currentL` _int_<br>
The current subgroup length for the transformation step.

## Kernel_MultiplyNtt_Transpose(uint[], uint[], int, int)

A GPU kernel (`[GpuKernel]`) that transposes the NTT matrix.

```csharp
[GpuKernel]
public static void Kernel_MultiplyNtt_Transpose(uint[] input, uint[] output, int width, int height);
```

#### Parameters

`input` _uint[]_<br>
The input matrix buffer.

`output` _uint[]_<br>
The output transposed matrix buffer.

`width` _int_<br>
The width of the input matrix.

`height` _int_<br>
The height of the input matrix.

## Kernel_MultiplyNtt_PointwiseMultiply(uint[], uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that performs pointwise modular multiplication in the frequency domain.

```csharp
[GpuKernel]
public static void Kernel_MultiplyNtt_PointwiseMultiply(
    uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B, int transformLength);
```

#### Parameters

`nttBuf1A` _uint[]_<br>
The transformed operand A buffer for prime 1. It also stores the product result.

`nttBuf2A` _uint[]_<br>
The transformed operand A buffer for prime 2. It also stores the product result.

`nttBuf1B` _uint[]_<br>
The transformed operand B buffer for prime 1.

`nttBuf2B` _uint[]_<br>
The transformed operand B buffer for prime 2.

`transformLength` _int_<br>
The length of the NTT transform.

## Kernel_MultiplyNtt_ComputeCRT(uint[], uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that computes the Chinese Remainder Theorem (CRT) coefficients to recombine NTT results.

```csharp
[GpuKernel]
public static void Kernel_MultiplyNtt_ComputeCRT(
    uint[] deviceResult1, uint[] deviceResult2, uint[] uncarriedLow, uint[] uncarriedHigh, int transformLength);
```

#### Parameters

`deviceResult1` _uint[]_<br>
The pointwise multiplication result in the frequency domain for prime 1.

`deviceResult2` _uint[]_<br>
The pointwise multiplication result in the frequency domain for prime 2.

`uncarriedLow` _uint[]_<br>
The output buffer for the lower 32-bits of the uncarried coefficients.

`uncarriedHigh` _uint[]_<br>
The output buffer for the upper 32-bits of the uncarried coefficients.

`transformLength` _int_<br>
The length of the NTT transform.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.BigInt.Kernels;

// Execution Context: G# Kernel (GPU)
// This is an internal Kernel class and its methods are launched via the G# execution pipeline.
Console.WriteLine("GpuBigIntMulKernels contains static kernel endpoints for NTT.");
```

## Remarks

This class contains `[GpuKernel]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuContext`.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuContext](../../../../03_bcl_api/math_bignum/gpu_context/GpuContext.md)

### BCL API > GPU Class

- [Gpu Class](../../../../03_bcl_api/gpu_class/00_index.md)
- [InlinePtx](../../../../03_bcl_api/gpu_class/InlinePtx.md)

### Guides

- [4. Practical Guides and Best Practices](../../../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInteger with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
- [7. Stdlib Math and Lazy Evaluation](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
