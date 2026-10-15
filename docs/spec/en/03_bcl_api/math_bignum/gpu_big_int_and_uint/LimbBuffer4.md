---
title: "LimbBuffer4 and DummyUIntEnumerator Structs (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "128-bit (4 limbs x 32-bit) zero-allocation inline array buffer and C# 12 collection builder support enumerator."
---

## LimbBuffer4 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines a 128-bit (4 limbs $\times$ 32-bit words) zero-allocation inline array buffer used by [`GpuUInt128`](GpuUInt128.md) and [`GpuBigUInt<LimbBuffer4>`](GpuBigUIntGeneric.md), alongside `DummyUIntEnumerator` for C# 12 collection expression support.

```csharp
[InlineArray(4)]
public struct LimbBuffer4

[ExcludeFromCodeCoverage]
public struct DummyUIntEnumerator
{
    public uint Current => 0;
    public bool MoveNext() => false;
}
```

## Types in `LimbBuffers.cs`

| Name | Description |
| --- | --- |
| `LimbBuffer4` | A 128-bit (`[InlineArray(4)]`) contiguous 4-word `uint` value-type buffer. |
| [`LimbBuffer8`](LimbBuffer8.md) | A 256-bit (`[InlineArray(8)]`) contiguous 8-word `uint` value-type buffer. |
| [`LimbBuffer16`](LimbBuffer16.md) | A 512-bit (`[InlineArray(16)]`) contiguous 16-word `uint` value-type buffer. |
| `DummyUIntEnumerator` | Compile-time dummy enumerator struct satisfying the C# 12 `CollectionBuilderAttribute` element type requirement (`CS9188`). |

## Remarks

Backed by C# 12 `[InlineArray(4)]`, `LimbBuffer4` allocates contiguous stack or GPU register/local memory without managed heap allocations.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Create a 4-limb inline array buffer (128-bit)
var buffer = new LimbBuffer4();

// Since it's an InlineArray, it can be accessed like a span or array
buffer[0] = 0x01;
buffer[1] = 0x02;
buffer[2] = 0x03;
buffer[3] = 0x04;

uint sum = 0;
foreach (var limb in buffer)
{
    sum += limb;
}

Console.WriteLine($"Sum of 4 limbs: {sum}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuBigUInt](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuBigUInt.md)
- [GpuBigUInt<TSize>](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuBigUIntGeneric.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
