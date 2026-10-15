---
title: "GpuBigIntContext and Algorithm Context Structs (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Context parameter structs for BigInt GPU kernels, Newton-Raphson mega-kernels, NTT, and RNS operations."
---

## GpuBigIntContext and Algorithm Context Structs

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Context parameter structures for basic `GpuBigInt` GPU kernels, `MacroTranspiler` expansions, Newton-Raphson mega-kernels (`SqrtNewtonRaphson`, `ReciprocalNewtonRaphson`, `ModPowNttContext`), and Residue Number System (RNS) modular reductions.

> [!NOTE]
> `BigIntKernelContext` was streamlined to `GpuBigIntContext` (with top-level `OperandContext`, `BufferContext`, `DimensionContext`, `TwiddleFactorContext`, and `WorkspaceStateContext` structs in `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`).

```csharp
public struct GpuBigIntContext
public struct OperandContext
public struct BufferContext
public struct TwiddleFactorContext
public struct DimensionContext
public struct WorkspaceStateContext
public struct SqrtNewtonRaphson
public struct ReciprocalNewtonRaphson
public struct ModPowNttContext
public struct RnsConstantsContext
public struct RnsBigInt
public struct RnsMontgomeryContext
```

## Structs

| Name | Description |
| --- | --- |
| [GpuBigIntContext](#gpubigintcontext-struct) | Context parameters for basic BigInt GPU kernels and `MacroTranspiler` expansions. |
| [OperandContext](#operandcontext-struct) | Encapsulates input operands, constant parameters, and destination buffers for arithmetic kernels. |
| [BufferContext](#buffercontext-struct) | Encapsulates device scratch buffers and intermediate memory arrays for multi-precision calculations. |
| [TwiddleFactorContext](#twiddlefactorcontext-struct) | Encapsulates precomputed forward and inverse NTT twiddle factors for multi-prime moduli. |
| [DimensionContext](#dimensioncontext-struct) | Encapsulates multi-precision word sizes, bit widths, and iteration counts for big integer algorithms. |
| [WorkspaceStateContext](#workspacestatecontext-struct) | Encapsulates the complete workspace scratch memory state for Newton-Raphson mega-kernels. |
| [SqrtNewtonRaphson](#sqrtnewtonraphson-struct) | Context parameters for the Newton-Raphson Sqrt MegaKernel. |
| [ReciprocalNewtonRaphson](#reciprocalnewtonraphson-struct) | Context parameters for the Newton-Raphson Reciprocal (`DivRem`) MegaKernel. |
| [ModPowNttContext](#modpownttcontext-struct) | Encapsulates parameters and scratch state for NTT-based modular exponentiation. |
| [RnsConstantsContext](#rnsconstantscontext-struct) | Precomputed constants for RNS (Residue Number System) operations. |
| [RnsBigInt](#rnsbigint-struct) | Represents a multi-precision integer decomposed into Residue Number System spaces. |
| [RnsMontgomeryContext](#rnsmontgomerycontext-struct) | Encapsulates dynamic constants dependent on the modulus $N$ for Montgomery reduction. |

## GpuBigIntContext Struct

Context parameters for basic BigInt GPU kernels and `MacroTranspiler` expansions.

```csharp
public struct GpuBigIntContext
{
    public OperandContext Operands;
    public int NumberOfWords;
}
```

## OperandContext Struct

Encapsulates input operands, constant parameters, and destination buffers for arithmetic kernels.

```csharp
public struct OperandContext
{
    [GpuReadOnly] public uint[] LeftOperand;
    [GpuReadOnly] public uint[] RightOperand;
    public uint[] TargetOperand;
    public uint[] ResultBuffer;
    public uint[] Constant;
    public uint[] Constant3;
    public long[] CarryBuffer;
}
```

## BufferContext Struct

Encapsulates device scratch buffers and intermediate memory arrays for multi-precision calculations.

```csharp
public struct BufferContext
{
    public uint[] Buffer1;
    public uint[] Buffer2;
    public uint[] Buffer3;
    public uint[] ValueT;
    public uint[] Quotient;
    public uint[] Remainder;
    public uint[] RemainderMask;
    public uint[] TemporaryBuffer;
    public long[] CarryBuffer;
}
```

## TwiddleFactorContext Struct

Encapsulates precomputed forward and inverse NTT twiddle factors for multi-prime moduli.

```csharp
public struct TwiddleFactorContext
{
    [GpuReadOnly] public uint[] Forward1;
    [GpuReadOnly] public uint[] Forward2;
    [GpuReadOnly] public uint[] Forward3;
    [GpuReadOnly] public uint[] Inverse1;
    [GpuReadOnly] public uint[] Inverse2;
    [GpuReadOnly] public uint[] Inverse3;
}
```

## DimensionContext Struct

Encapsulates multi-precision word sizes, bit widths, and iteration counts for big integer algorithms.

```csharp
public struct DimensionContext
{
    public int NumberOfWords;
    public int NumberOfWordsExtended;
    public int Exponent;
    public int BitOffset2E1;
    public int MarginWords;
    public int ActualBits;
    public int MarginBits;
    public int Iterations;
    public int TargetBits;
    public int NumBits;
}
```

## WorkspaceStateContext Struct

Encapsulates the complete workspace scratch memory state for Newton-Raphson mega-kernels.

```csharp
public struct WorkspaceStateContext
{
    public uint[] XSquared;
    public uint[] AxSquared;
    public uint[] CurrentProduct;
    public uint[] Diff;
    public uint[] NextXUnshifted;
    public uint[] NextXShifted;
    public uint[] NextIter;
    public uint[] NttBuf1A;
    public uint[] NttBuf2A;
    public uint[] NttBuf1B;
    public uint[] NttBuf2B;
    public uint[] TmpBuf;
    public uint[] UncarriedLow;
    public uint[] UncarriedHigh;
    public uint[] BlockCarries;
    public uint[] ModulusExpanded;
}
```

## SqrtNewtonRaphson Struct

Context parameters for the Newton-Raphson Sqrt MegaKernel.

```csharp
public struct SqrtNewtonRaphson
{
    public OperandContext Operands;
    public DimensionContext Dimensions;
    public Ntt Ntt;
    public WorkspaceStateContext Workspace;
    public long[] CarryBuffer;
}
```

## ReciprocalNewtonRaphson Struct

Context parameters for the Newton-Raphson Reciprocal (`DivRem`) MegaKernel.

```csharp
public struct ReciprocalNewtonRaphson
{
    public OperandContext Operands;
    public DimensionContext Dimensions;
    public Ntt Ntt;
    public WorkspaceStateContext Workspace;
    public long[] CarryBuffer;
}
```

## ModPowNttContext Struct

Encapsulates parameters and scratch state for NTT-based modular exponentiation.

```csharp
public struct ModPowNttContext
{
    public OperandContext Operands;
    public uint[] ReciprocalMod;
    public uint[] Exponent;
    public int ExponentWords;
    public int ExponentStartWord;
    public int ExponentEndWord;
    public DimensionContext Dimensions;
    public Ntt Ntt;
    public WorkspaceStateContext Workspace;
    public long[] CarryBuffer;
}
```

## RnsConstantsContext Struct

Precomputed constants for RNS (Residue Number System) operations.

```csharp
public struct RnsConstantsContext
{
    [GpuReadOnly] public uint[] ModuliB1;
    [GpuReadOnly] public uint[] ModuliB2;
    public uint ModSk;
    [GpuReadOnly] public uint[] GammaB1;
    [GpuReadOnly] public uint[] GammaB2;
    [GpuReadOnly] public uint[] M1OverMiModMsk;
    public uint M1InvModMsk;
    [GpuReadOnly] public uint[] M2OverMjModMsk;
    public uint M2InvModMsk;
    [GpuReadOnly] public uint[] M1OverMiModMj;
    [GpuReadOnly] public uint[] M2OverMjModMi;
    [GpuReadOnly] public uint[] M1ModMj;
    [GpuReadOnly] public uint[] M2ModMi;
    public int KSize;
}
```

## RnsBigInt Struct

Represents a multi-precision integer decomposed into Residue Number System spaces.

```csharp
public struct RnsBigInt
{
    public uint[] B1;
    public uint[] B2;
    public uint Sk;
}
```

## RnsMontgomeryContext Struct

Encapsulates dynamic constants dependent on the modulus $N$ for Montgomery reduction.

```csharp
public struct RnsMontgomeryContext
{
    [GpuReadOnly] public uint[] NRnsB2;
    public uint NSk;
    [GpuReadOnly] public uint[] NegNInvB1;
    [GpuReadOnly] public uint[] RInvB2;
}
```

## Examples

### Creating a GpuBigIntContext

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Create and initialize a context for a basic BigInt kernel
var context = new GpuBigIntContext
{
    NumberOfWords = 4,
    Operands = new OperandContext
    {
        LeftOperand = [1, 2, 3, 4],
        RightOperand = [5, 6, 7, 8],
        ResultBuffer = new uint[4],
        CarryBuffer = new long[4]
    }
};

Console.WriteLine($"Kernel Context created for {context.NumberOfWords} words.");
Console.WriteLine($"LeftOperand Length: {context.Operands.LeftOperand.Length}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [Ntt and DualNtt Structs](NttContext.md)
- [Fermat Struct](FermatContext.md)
- [NumberTheoreticTransform Struct](NumberTheoreticTransformKernelArgs.md)
