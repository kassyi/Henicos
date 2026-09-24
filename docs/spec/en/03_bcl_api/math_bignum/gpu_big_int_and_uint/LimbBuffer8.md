---
title: "LimbBuffer8 Struct (G#)"
site: "G#"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A 256-bit (8 limbs * 32-bit) zero-allocation inline array buffer."
---

## LimbBuffer8 Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

A 256-bit (8 limbs * 32-bit) zero-allocation inline array buffer.

```csharp
[InlineArray(8)]
public struct LimbBuffer8
```

## Remarks

No remarks available.

## Examples

### Iterating Over a LimbBuffer

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Create an 8-limb inline array buffer (256-bit)
var buffer = new LimbBuffer8();

// Since it's an InlineArray, it can be accessed like a span or array
buffer[0] = 0x01;
buffer[7] = 0x08;

uint sum = 0;
foreach (var limb in buffer)
{
    sum += limb;
}

// Outputs: Sum of 8 limbs: 9
Console.WriteLine($"Sum of 8 limbs: {sum}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuUIntBigInteger](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuUIntBigInteger.md)
- [GpuUIntBigInteger<T>](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuUIntBigIntegerGeneric.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
