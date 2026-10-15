---
title: "NttMulContext Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Encapsulates parameter buffers, dimensions, and twiddle factor pointers for NTT-based big integer multiplication kernels."
---

## NttMulContext Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Encapsulates parameter buffers, dimensions, and twiddle factor pointers for NTT-based big integer multiplication kernels.

```csharp
public struct NttMulContext
{
    public OperandsBuffers Operands;
    public NttBuffers Ntt;
    public NttTwiddleFactors TwiddleFactors;
    public NttMulDimensions Dimensions;
}
```

## Nested Structs

| Name | Description |
| --- | --- |
| [NttMulContext.OperandsBuffers](#operandsbuffers-struct) | Contains device pointers for input operands (`A`, `B`) and destination buffer (`Result`). |
| [NttMulContext.NttBuffers](#nttbuffers-struct) | Contains device scratch buffers used during forward/inverse NTT convolutions. |
| [NttMulContext.NttTwiddleFactors](#ntttwiddlefactors-struct) | Contains precomputed roots of unity and twiddle factor tables. |
| [NttMulContext.NttMulDimensions](#nttmuldimensions-struct) | Specifies the word dimensions and transformation length for the multiplication. |

## OperandsBuffers Struct

Contains device pointers for input operands and destination buffer.

```csharp
public struct OperandsBuffers
{
    [GpuReadOnly] public uint[] A;
    [GpuReadOnly] public uint[] B;
    public uint[] Result;
}
```

## NttBuffers Struct

Contains device scratch buffers used during forward/inverse NTT convolutions.

```csharp
public struct NttBuffers
{
    public uint[] Buffer1A;
    public uint[] Buffer2A;
    public uint[] Buffer1B;
    public uint[] Buffer2B;
    public uint[] TemporaryBuffer;
}
```

## NttTwiddleFactors Struct

Contains precomputed roots of unity and twiddle factor tables.

```csharp
public struct NttTwiddleFactors
{
    [GpuReadOnly] public uint[] Forward1;
    [GpuReadOnly] public uint[] Forward2;
    [GpuReadOnly] public uint[] Inverse1;
    [GpuReadOnly] public uint[] Inverse2;
}
```

## NttMulDimensions Struct

Specifies the word dimensions and transformation length for the multiplication.

```csharp
public struct NttMulDimensions
{
    public int NumWordsA;
    public int NumWordsB;
    public int OutNumWords;
    public int TransformLength;
}
```

## Examples

### Configuring NTT Multiplication Dimensions

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

var context = new NttMulContext
{
    Dimensions = new NttMulContext.NttMulDimensions
    {
        NumWordsA = 16,
        NumWordsB = 16,
        OutNumWords = 32,
        TransformLength = 64
    }
};

Console.WriteLine($"NTT Multiplication Input Size: {context.Dimensions.NumWordsA}x{context.Dimensions.NumWordsB}");
Console.WriteLine($"Expected Output Size: {context.Dimensions.OutNumWords}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
