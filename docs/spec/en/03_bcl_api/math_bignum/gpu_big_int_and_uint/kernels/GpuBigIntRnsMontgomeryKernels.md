---
title: "GpuBigIntRnsMontgomeryKernels Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for carry-free modular exponentiation using Residue Number System (RNS) Montgomery reduction."
---

## GpuBigIntRnsMontgomeryKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Implements fully parallel, carry-free modular multiplication and modular exponentiation using Residue Number System (RNS) Montgomery reduction with Shenoy-Kumaresan base extension on the GPU.

```csharp
[SkipSnapshotConsistency("WIP - Phase 2 RNS Montgomery")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntRnsMontgomeryKernels
```

## Methods

| Name | Description |
| --- | --- |
| [ExecuteRnsMontgomeryMultiply(RnsBigInt, RnsBigInt, RnsMontgomeryContext, RnsConstantsContext, RnsBigInt)](#executernsmontgomerymultiplyrnsbigint-rnsbigint-rnsmontgomerycontext-rnsconstantscontext-rnsbigint) | A GPU device function (`[GpuDeviceFunction]`) that performs parallel RNS Montgomery multiplication across dual RNS bases (`B1`, `B2`) and a redundant Shenoy-Kumaresan modulus (`Sk`). |
| [ExecuteModPowRns(RnsBigInt, RnsBigInt, uint[], int, RnsMontgomeryContext, RnsConstantsContext, RnsBigInt, RnsBigInt, RnsBigInt)](#executemodpowrnsrnsbigint-rnsbigint-uint-int-rnsmontgomerycontext-rnsconstantscontext-rnsbigint-rnsbigint-rnsbigint) | A GPU kernel (`[GpuKernel]`) that computes constant-time square-and-multiply modular exponentiation in RNS Montgomery space. |

## ExecuteRnsMontgomeryMultiply(RnsBigInt, RnsBigInt, RnsMontgomeryContext, RnsConstantsContext, RnsBigInt)

A GPU device function (`[GpuDeviceFunction]`) that performs parallel RNS Montgomery multiplication across dual RNS bases (`B1`, `B2`) and a redundant Shenoy-Kumaresan modulus (`Sk`).

```csharp
[GpuDeviceFunction]
public static void ExecuteRnsMontgomeryMultiply(
    RnsBigInt a,
    RnsBigInt b,
    RnsMontgomeryContext montCtx,
    RnsConstantsContext ctx,
    RnsBigInt result);
```

#### Parameters

`a` _RnsBigInt_<br>
The first RNS operand in Montgomery form.

`b` _RnsBigInt_<br>
The second RNS operand in Montgomery form.

`montCtx` _RnsMontgomeryContext_<br>
The modulus-dependent RNS Montgomery constants ($N$ in base 2, $-N^{-1}$ in base 1, and $R^{-1}$ in base 2).

`ctx` _RnsConstantsContext_<br>
The precomputed RNS base extension constants and moduli for bases `B1`, `B2`, and `ModSk`.

`result` _RnsBigInt_<br>
The output RNS buffer receiving $a \cdot b \cdot M_1^{-1} \bmod N$.

## ExecuteModPowRns(RnsBigInt, RnsBigInt, uint[], int, RnsMontgomeryContext, RnsConstantsContext, RnsBigInt, RnsBigInt, RnsBigInt)

A GPU kernel (`[GpuKernel]`) that computes constant-time square-and-multiply modular exponentiation in RNS Montgomery space and converts the final result back to normal RNS representation.

```csharp
[GpuKernel]
public static void ExecuteModPowRns(
    RnsBigInt baseRns,
    RnsBigInt oneRns,
    [GpuReadOnly] uint[] exponent,
    int exponentWords,
    RnsMontgomeryContext montCtx,
    RnsConstantsContext ctx,
    RnsBigInt result,
    RnsBigInt x,
    RnsBigInt temp);
```

#### Parameters

`baseRns` _RnsBigInt_<br>
The base operand pre-converted to RNS Montgomery space ($\text{Base} \cdot M_1 \bmod N$).

`oneRns` _RnsBigInt_<br>
The multiplicative identity `1` in RNS Montgomery space ($M_1 \bmod N$).

`exponent` _uint[]_<br>
The exponent word array in little-endian limb order.

`exponentWords` _int_<br>
The number of 32-bit words in `exponent`.

`montCtx` _RnsMontgomeryContext_<br>
The modulus-dependent RNS Montgomery constants.

`ctx` _RnsConstantsContext_<br>
The precomputed RNS base extension constants and moduli.

`result` _RnsBigInt_<br>
The destination RNS buffer receiving the final modular exponentiation result.

`x` _RnsBigInt_<br>
Scratch RNS workspace buffer 1.

`temp` _RnsBigInt_<br>
Scratch RNS workspace buffer 2.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// RnsConstantsContext and RnsBigInt configure the RNS bases for ExecuteModPowRns.
var rnsCtx = new RnsConstantsContext { KSize = 32, ModSk = 0xFFFFFFFFu };
Console.WriteLine($"{nameof(GpuBigIntRnsMontgomeryKernels)} RNS KSize={rnsCtx.KSize}");
```

## Remarks

`GpuBigIntRnsMontgomeryKernels` targets intermediate operand sizes (typically 256 to 4096 bits) where NTT convolution overhead is excessive, but sequential division/carry propagation stalls GPU warps. Each thread in a block handles one RNS channel $i \in [0, K-1]$ in parallel.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [ModPow](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/algorithms/ModPow.md)
