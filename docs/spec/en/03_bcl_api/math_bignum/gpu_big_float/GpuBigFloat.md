---
title: "GpuBigFloat Struct (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for arbitrary-precision floating-point numbers on the GPU."
---

## GpuBigFloat Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigFloat`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents an arbitrary-precision floating-point number.

Similar to `GpuBigInteger`, this struct is a Host-Side wrapper that orchestrates operations by pairing an underlying unsigned magnitude (mantissa) with an exponent and a sign bit. 

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigFloat(GpuUIntBigInteger, long, bool)](#gpubigfloat-gpuuintbiginteger-long-bool) | Initializes a new floating point wrapper. |

## GpuBigFloat(GpuUIntBigInteger, long, bool)

Initializes a new wrapper around a raw mantissa buffer and a scalar exponent.

```csharp
public GpuBigFloat(GpuUIntBigInteger mantissa, long exponent, bool isNegative = false);
```

#### Parameters

`mantissa` _GpuUIntBigInteger_<br>
The underlying magnitude residing on the GPU.

`exponent` _long_<br>
The base-2 exponent.

`isNegative` _bool_<br>
`true` if the floating-point number is negative; otherwise, `false`.

## Properties

| Name | Description |
| --- | --- |
| `Mantissa` | Gets the underlying unsigned magnitude (`GpuUIntBigInteger`) on the GPU. |
| `Exponent` | Gets the base-2 exponent. |
| `IsNegative` | Gets a value indicating whether the number is strictly negative. |

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Disposes the underlying GPU mantissa buffer. |

## Dispose()

Disposes the underlying GPU mantissa buffer (`Mantissa`).

```csharp
public void Dispose();
```

## Examples

### Constructing a GpuBigFloat

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 1024);

// Create a mantissa for the value 1.5 (e.g. 3 * 2^-1)
var mantissa = GpuUIntBigInteger.FromBigInteger(context, 3);
long exponent = -1;

var floatVal = new GpuBigFloat(mantissa, exponent, isNegative: false);

Console.WriteLine($"IsNegative: {floatVal.IsNegative}");
```

## Remarks

Unlike hardware IEEE 754 floats, `GpuBigFloat` separates the mantissa and exponent entirely. The GPU kernels only perform unsigned integer math on the `Mantissa` buffer, while the Host CPU orchestrates the alignment of the `Exponent` during addition/subtraction.

For more information on the separation of Signed and Unsigned components to prevent Warp Divergence, see [The Architecture: Signed vs. Unsigned Separation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md#3-the-architecture-signed-vs-unsigned-separation).

## See also

### BigFloat APIs
- [GpuBigFloat Overview](00_index.md)
- [Operators](Operators.md)

### Related Bignum Types
- [GpuBigInteger](../gpu_big_int_and_uint/GpuBigInteger.md)
- [GpuUIntBigInteger](../gpu_big_int_and_uint/GpuUIntBigInteger.md)
- [GpuContext](../gpu_context/GpuContext.md)

### BCL & Architecture
- [Arbitrary-Precision Math Overview](../00_index.md)
- [Gpu BCL API Overview](../../00_index.md)
- [4. Bignum Transpiler Internals](../../../02_architecture/04_bignum_internals.md)

### Guides
- [Stdlib Math and Lazy Evaluation Guide](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [Precision and Performance Guide](../../../04_guides/05_precision_and_performance_guide.md)
