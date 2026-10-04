---
title: "BigIntegerExtensions Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Provides mathematical extension methods for System.Numerics.BigInteger."
---

## BigIntegerExtensions Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides mathematical extension methods for `BigInteger`.

```csharp
public static class BigIntegerExtensions
```

## Methods

| Name | Description |
| --- | --- |
| [Sqrt(this BigInteger)](#sqrtthis-biginteger) | Computes the integer square root (`floor(sqrt(value))`) of the specified `BigInteger` using Heron's method (Newton-Raphson). |

## Sqrt(this BigInteger)

Computes the integer square root (`floor(sqrt(value))`) of the specified `BigInteger` using Heron's method (Newton-Raphson).

```csharp
public static BigInteger Sqrt(this BigInteger value);
```

#### Parameters

`value` _BigInteger_<br>
The non-negative big integer whose integer square root is to be computed.

#### Returns

_BigInteger_<br>
The integer square root of `value`.

#### Exceptions

`ArgumentOutOfRangeException`<br>
Thrown if `value` is negative.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)

using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

var bigValue = BigInteger.Parse("123456789012345678901234567890");
var root = bigValue.Sqrt();
Console.WriteLine(root);
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
