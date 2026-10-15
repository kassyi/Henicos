---
title: "GpuBigIntFermatKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Fermat number related operations and reductions."
---

## GpuBigIntFermatKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and device functions used for Fermat number arithmetic, primarily large modular reductions against Fermat primes and shared-memory matrix transpositions on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntFermatKernels
```

## Nested Types

| Name | Description |
| --- | --- |
| `FermatReduceContext` | Encapsulates the device buffers and shift/word parameters for the mega Fermat modular reduction kernel. |

## Methods

| Name | Description |
| --- | --- |
| [DeviceUnpack16(uint[], uint[], uint[], int, int, bool)](#deviceunpack16uint-uint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that unpacks 32-bit words into 16-bit elements across dual NTT buffers. |
| [DeviceTranspose32X32(uint[], uint[], int, int, bool)](#devicetranspose32x32uint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that transposes a 2D matrix using 32x32 shared-memory tiles with bank-conflict padding. |
| [DeviceShiftLeftParallel(uint[], uint[], int, int, bool)](#deviceshiftleftparalleluint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs a parallel grid-stride left bit-shift across device buffers. |
| [KernelTestBlockAddSubParallel(uint[], uint[], uint[], int, int)](#kerneltestblockaddsubparalleluint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) entry point that invokes `GpuBigIntKernels.DeviceAddSubParallel` for block-level parallel addition or subtraction. |
| [KernelFermatReduceMega(FermatReduceContext)](#kernelfermatreducemegafermatreducecontext) | A GPU kernel (`[GpuKernel]`) that performs a large multi-pass modular reduction using Fermat number properties. |

## FermatReduceContext Struct

Encapsulates the device buffers and shift/word parameters for the mega Fermat modular reduction kernel.

```csharp
public struct FermatReduceContext
{
    public uint[] ValueT;
    public uint[] MulResult;
    public uint[] Quotient;
    public uint[] Remainder;
    public uint[] Temp;
    [GpuReadOnly] public uint[] RemainderMask;
    public int NumWords;
    public int OutNumWords;
    public int TotalShiftBits;
    public int Exponent;
}
```

## DeviceUnpack16(uint[], uint[], uint[], int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that unpacks 32-bit words into 16-bit elements across dual NTT buffers.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceUnpack16([GpuReadOnly] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords, bool syncThreads = true);
```

#### Parameters

`dSrc` _uint[]_<br>
The source 32-bit word buffer.

`dDst1` _uint[]_<br>
The first destination NTT buffer receiving 16-bit unpacked elements.

`dDst2` _uint[]_<br>
The second destination NTT buffer receiving 16-bit unpacked elements.

`n` _int_<br>
The transform length (total number of 16-bit elements).

`numWords` _int_<br>
The number of 32-bit words in `dSrc`.

`syncThreads` _bool_<br>
If `true`, synchronizes threads within the block upon completion.

## DeviceTranspose32X32(uint[], uint[], int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that transposes a 2D matrix using 32x32 shared-memory tiles with bank-conflict padding.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceTranspose32X32([GpuReadOnly] uint[] idata, uint[] odata, int width, int height, bool syncThreads = true);
```

#### Parameters

`idata` _uint[]_<br>
The input matrix buffer of dimensions `height` x `width`.

`odata` _uint[]_<br>
The output transposed matrix buffer of dimensions `width` x `height`.

`width` _int_<br>
The width (column count) of the input matrix.

`height` _int_<br>
The height (row count) of the input matrix.

`syncThreads` _bool_<br>
If `true`, synchronizes threads after each tile write.

## DeviceShiftLeftParallel(uint[], uint[], int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs a parallel grid-stride left bit-shift across device buffers.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceShiftLeftParallel([GpuReadOnly] uint[] src, uint[] dst, int shiftBits, int numWords, bool syncThreads = true);
```

#### Parameters

`src` _uint[]_<br>
The source word buffer.

`dst` _uint[]_<br>
The destination word buffer.

`shiftBits` _int_<br>
The total number of bits to shift left.

`numWords` _int_<br>
The total number of words to process.

`syncThreads` _bool_<br>
If `true`, synchronizes threads within the block upon completion.

## KernelTestBlockAddSubParallel(uint[], uint[], uint[], int, int)

A GPU kernel (`[GpuKernel]`) entry point that invokes `GpuBigIntKernels.DeviceAddSubParallel` for block-level parallel addition or subtraction.

```csharp
[GpuKernel]
public static void KernelTestBlockAddSubParallel([GpuReadOnly] uint[] a, [GpuReadOnly] uint[] b, uint[] result, int numWords, int sign);
```

#### Parameters

`a` _uint[]_<br>
The first operand buffer.

`b` _uint[]_<br>
The second operand buffer.

`result` _uint[]_<br>
The output result buffer.

`numWords` _int_<br>
The number of 32-bit words to process.

`sign` _int_<br>
`1` for addition (`a + b`), or `-1` for subtraction (`a - b`).

## KernelFermatReduceMega(FermatReduceContext)

A GPU kernel (`[GpuKernel]`) that performs a large multi-pass modular reduction using Fermat number properties.

```csharp
[GpuKernel]
public static void KernelFermatReduceMega(FermatReduceContext ctx);
```

#### Parameters

`ctx` _FermatReduceContext_<br>
The reduction context containing source, quotient, remainder, mask buffers, and bit-shift parameters.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// Configure a FermatReduceContext for launching KernelFermatReduceMega via the G# execution pipeline.
var ctx = new GpuBigIntFermatKernels.FermatReduceContext
{
    NumWords = 64,
    OutNumWords = 64,
    TotalShiftBits = 1024,
    Exponent = 20
};
Console.WriteLine($"FermatReduceContext configured: NumWords={ctx.NumWords}, Exponent={ctx.Exponent}");
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
