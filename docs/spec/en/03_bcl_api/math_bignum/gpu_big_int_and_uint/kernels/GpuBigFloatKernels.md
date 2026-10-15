---
title: "GpuBigFloatKernels Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for arbitrary-precision floating-point aligned addition and normalization."
---

## GpuBigFloatKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and context structures for arbitrary-precision floating-point aligned addition/subtraction and significand normalization.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
public static partial class GpuBigFloatKernels
```

## Nested Types

| Name | Description |
| --- | --- |
| `GpuBigFloatKernels.GpuBigFloatAddContext` | Parameter context struct for `FloatAddAlignedKernel` holding larger/smaller significand buffers, word/bit shifts, and sign scale. |

### GpuBigFloatAddContext Struct

```csharp
public struct GpuBigFloatAddContext
{
    [GpuReadOnly] public uint[] LargerSig;
    public int LargerWords;
    [GpuReadOnly] public uint[] SmallerSig;
    public int SmallerWords;
    public uint[] ResultSig;
    public int NumWords;
    public int WordShift;
    public int BitShift;
    public int SignScale;
}
```

## Methods

| Name | Description |
| --- | --- |
| [FloatAddAlignedKernel(GpuBigFloatAddContext)](#floataddalignedkernelgpubigfloataddcontext) | A GPU kernel (`[GpuKernel]`) that performs radix-aligned addition or subtraction of two floating-point significands using warp-level prefix scan carry propagation. |
| [FloatNormalizeKernel(uint[], int, uint[], int, int, int)](#floatnormalizekerneluint-int-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that shifts a floating-point significand by the specified word and bit shift to normalize its leading limb. |

## FloatAddAlignedKernel(GpuBigFloatAddContext)

A GPU kernel (`[GpuKernel]`) that performs radix-aligned addition or subtraction of two floating-point significands using warp-level prefix scan carry propagation.

```csharp
[GpuKernel]
public static void FloatAddAlignedKernel(GpuBigFloatAddContext ctx);
```

#### Parameters

`ctx` _GpuBigFloatAddContext_<br>
The aligned addition context struct.

## FloatNormalizeKernel(uint[], int, uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that shifts a floating-point significand by the specified word and bit shift to normalize its leading limb.

```csharp
[GpuKernel]
public static void FloatNormalizeKernel(
    [GpuReadOnly] uint[] valueSig, int valueWords,
    uint[] resultSig, int resultWords,
    int wordShift, int bitShift);
```

#### Parameters

`valueSig` _uint[]_<br>
The input significand limb buffer.

`valueWords` _int_<br>
The number of words in `valueSig`.

`resultSig` _uint[]_<br>
The destination normalized significand buffer.

`resultWords` _int_<br>
The number of words in `resultSig`.

`wordShift` _int_<br>
The number of 32-bit words to shift.

`bitShift` _int_<br>
The intra-word bit shift ($0 \le \text{bitShift} < 32$).

## Examples

### Constructing a GpuBigFloatAddContext

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

var ctx = new GpuBigFloatKernels.GpuBigFloatAddContext
{
    LargerWords = 8,
    SmallerWords = 8,
    NumWords = 8,
    WordShift = 0,
    BitShift = 2,
    SignScale = 1
};

Console.WriteLine($"Context configured for {ctx.NumWords} words, BitShift={ctx.BitShift}");
```

## See also

### BCL API > Math Bignum
- [GpuBigFloat Struct](../../gpu_big_float/GpuBigFloat.md)
- [IGpuBigFloatArithmetic Interface](../../gpu_big_float/IGpuBigFloatArithmetic.md)
- [GpuBigIntKernels Class](GpuBigIntKernels.md)
