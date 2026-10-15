---
title: "Ntt and DualNtt Structs (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Context parameters for single-pass and dual-pass NTT operations in GPU big integer kernels."
---

## Ntt and DualNtt Structs

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Context parameters for NTT-related operations (`Ntt`, formerly `NttContext`) and dual-pass NTT GPU execution (`DualNtt`).

```csharp
public struct Ntt
{
    public int TransformLength;
    public BufferContext Buffers;
    public TwiddleFactorContext TwiddleFactors;
}

public struct DualNtt
{
    public uint[] Buffer1;
    public uint[] Buffer2;
    public uint[] TemporaryBuffer;
    [GpuReadOnly] public uint[] Twiddle1;
    [GpuReadOnly] public uint[] Twiddle2;
    public int M;
    public int N1;
    public int N2;
}
```

## Structs

| Name | Description |
| --- | --- |
| [Ntt](#ntt-struct) | Context parameters for NTT-related operations in Fermat and Newton-Raphson sequences. |
| [DualNtt](#dualntt-struct) | Context parameters for dual-pass NTT GPU execution. |

## Ntt Struct

Context parameters for NTT-related operations in Fermat and Newton-Raphson sequences.

```csharp
public struct Ntt
{
    public int TransformLength;
    public BufferContext Buffers;
    public TwiddleFactorContext TwiddleFactors;
}
```

## DualNtt Struct

Context parameters for dual-pass NTT GPU execution.

```csharp
public struct DualNtt
{
    public uint[] Buffer1;
    public uint[] Buffer2;
    public uint[] TemporaryBuffer;
    [GpuReadOnly] public uint[] Twiddle1;
    [GpuReadOnly] public uint[] Twiddle2;
    public int M;
    public int N1;
    public int N2;
}
```

## Examples

### Creating an Ntt Context

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

var context = new Ntt
{
    TransformLength = 1024,
    Buffers = new BufferContext
    {
        Buffer1 = new uint[1024],
        Buffer2 = new uint[1024],
        Buffer3 = new uint[1024],
        TemporaryBuffer = new uint[1024]
    }
};

Console.WriteLine($"NTT Transform Length: {context.TransformLength}");
Console.WriteLine($"Buffer1 Size: {context.Buffers.Buffer1.Length}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuBigIntContext and Algorithm Context Structs](BigIntKernelContext.md)
- [NumberTheoreticTransform Struct](NumberTheoreticTransformKernelArgs.md)
