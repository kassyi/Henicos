---
title: "NumberTheoreticTransform Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Execution arguments for NTT-related kernels."
---

## NumberTheoreticTransform Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Execution arguments for NTT-related kernels (formerly `NumberTheoreticTransformKernelArgs`).

```csharp
public struct NumberTheoreticTransform
{
    public uint[] Buffer;
    public uint[] TemporaryBuffer;
    [GpuReadOnly] public uint[] Twiddle;
    public NttGridContext Grid;
    public uint P;
    public ulong Pm;
    public bool IsInverse;
    public bool ApplyTwiddleBefore;
    public bool ApplyTwiddleAfter;
    public int MaxBlocks;
    public int CurrentL;
}
```

## Fields

| Name | Description |
| --- | --- |
| `Buffer` | In-place transform buffer. |
| `TemporaryBuffer` | Temporary scratch buffer. |
| `Twiddle` | Read-only twiddle factor table (`[GpuReadOnly]`). |
| `Grid` | The six-step NTT grid dimensions (`NttGridContext`: `M`, `N1`, `N2`, with $M = N_1 \times N_2$). |
| `P` | Prime modulus $P$. |
| `Pm` | Barrett reduction or multiplicative inverse parameter $P_m$. |
| `IsInverse` | Indicates whether an inverse NTT is performed. |
| `ApplyTwiddleBefore` | Indicates whether twiddle multiplication is applied before transform. |
| `ApplyTwiddleAfter` | Indicates whether twiddle multiplication is applied after transform. |
| `MaxBlocks` | The maximum number of GPU blocks to dispatch. |
| `CurrentL` | The current transform stage length $L$. |

## NttGridContext Struct

The 2D six-step NTT grid dimensions ($M = N_1 \times N_2$), shared by `NumberTheoreticTransform` and [`DualNtt`](NttContext.md#dualntt-struct).

```csharp
public struct NttGridContext
{
    public int M;
    public int N1;
    public int N2;
}
```

## Examples

### Preparing Arguments for NTT Kernel Launch

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

var kernelArgs = new NumberTheoreticTransform
{
    Buffer = new uint[256],
    Twiddle = new uint[256],
    TemporaryBuffer = new uint[256],
    Grid = new NttGridContext { M = 256, N1 = 16, N2 = 16 },
    P = 0xFFFFFFFF,
    IsInverse = false
};

Console.WriteLine($"NTT Kernel Args - M: {kernelArgs.Grid.M}, N1: {kernelArgs.Grid.N1}, Inverse: {kernelArgs.IsInverse}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [Ntt and DualNtt Structs](NttContext.md)
