---
title: "NttMulContext Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
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
```

## Examples

### Configuring NTT Multiplication Dimensions

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

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

// Outputs: NTT Multiplication Input Size: 16x16
Console.WriteLine($"NTT Multiplication Input Size: {context.Dimensions.NumWordsA}x{context.Dimensions.NumWordsB}");
// Outputs: Expected Output Size: 32
Console.WriteLine($"Expected Output Size: {context.Dimensions.OutNumWords}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../../../02_architecture/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
