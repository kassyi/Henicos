---
title: "Fermat Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Context parameters for the Fermat sequence computation."
---

## Fermat Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Context parameters for the Fermat sequence computation (formerly `FermatContext`).

```csharp
public struct Fermat
{
    public BufferContext Buffers;
    public Ntt Ntt;
}
```

## Fields

| Name | Description |
| --- | --- |
| [Buffers](#buffers) | The scratch buffer context (`BufferContext`) for intermediate evaluations. |
| [Ntt](#ntt) | The NTT configuration and twiddle factor context (`Ntt`). |

## Buffers

The scratch buffer context for intermediate evaluations.

```csharp
public BufferContext Buffers;
```

## Ntt

The NTT configuration and twiddle factor context.

```csharp
public Ntt Ntt;
```

## Examples

### Creating a Fermat Context

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

var context = new Fermat
{
    Ntt = new Ntt
    {
        TransformLength = 1024,
        Buffers = new BufferContext { Buffer1 = new uint[1024] }
    }
};

Console.WriteLine($"Fermat NTT TransformLength: {context.Ntt.TransformLength}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuBigIntContext and Algorithm Context Structs](BigIntKernelContext.md)
- [Ntt and DualNtt Structs](NttContext.md)
