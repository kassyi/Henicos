---
title: "NumberTheoreticTransformKernelArgs Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Execution arguments for NTT-related kernels."
---

## NumberTheoreticTransformKernelArgs Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.Contexts`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Execution arguments for NTT-related kernels.

```csharp
public struct NumberTheoreticTransformKernelArgs
```

## Examples

### Preparing Arguments for NTT Kernel Launch

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.Contexts;

// Prepare arguments for an NTT Kernel launch
var kernelArgs = new NumberTheoreticTransformKernelArgs
{
    Buffer = new uint[256],
    Twiddle = new uint[256],
    TemporaryBuffer = new uint[256],
    M = 8,
    N1 = 16,
    N2 = 16,
    P = 0xFFFFFFFF,
    IsInverse = false
};

// Outputs: NTT Kernel Args - M: 8, N1: 16, Inverse: False
Console.WriteLine($"NTT Kernel Args - M: {kernelArgs.M}, N1: {kernelArgs.N1}, Inverse: {kernelArgs.IsInverse}");
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
- [Advanced: Extending GpuBigInteger with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
