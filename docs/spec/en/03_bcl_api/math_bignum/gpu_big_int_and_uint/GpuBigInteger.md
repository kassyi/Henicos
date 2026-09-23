---
title: "GpuBigInteger Struct (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for the Host-Side signed wrapper for massive arbitrary-precision integers."
---

## GpuBigInteger Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents a signed, arbitrary-precision integer in the GSharp ecosystem. 

> [!IMPORTANT]
> `GpuBigInteger` is a **Host-Side Only** wrapper. It cannot be used directly inside a `[GpuKernel]`. It is designed to evaluate sign operations on the CPU and orchestrate pure, unsigned math kernels on the GPU.

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigInteger(GpuUIntBigInteger, bool)](#gpubiginteger-gpuuintbiginteger-bool) | Creates a signed wrapper around an existing unsigned memory buffer. |

## GpuBigInteger(GpuUIntBigInteger, bool)

Creates a signed wrapper around an existing unsigned memory buffer.

```csharp
public GpuBigInteger(GpuUIntBigInteger value, bool isNegative = false);
```

#### Parameters

`value` _GpuUIntBigInteger_<br>
The underlying absolute value residing on the GPU. The wrapper takes ownership of this reference.

`isNegative` _bool_<br>
`true` if the number is strictly negative; otherwise, `false`.

## Properties

| Name | Description |
| --- | --- |
| `Value` | Gets the underlying absolute value buffer (`GpuUIntBigInteger`) on the GPU. |
| `IsNegative` | Gets a value indicating whether the integer is strictly negative. |

## Methods

| Name | Description |
| --- | --- |
| [Dispose()](#dispose) | Disposes the underlying GPU buffer. |
| [CompareTo(GpuBigInteger)](#compareto-gpubiginteger) | Compares the values of two instances. |

## Dispose()

Disposes the underlying GPU buffer (`Value`).

```csharp
public void Dispose();
```

## CompareTo(GpuBigInteger)

Compares the values of two `GpuBigInteger` instances on the host by dispatching a comparison kernel for the absolute values and evaluating the sign bits.

```csharp
public int CompareTo(GpuBigInteger other);
```

#### Parameters

`other` _GpuBigInteger_<br>
The value to compare with this instance.

#### Returns
_int_<br>
A signed integer that indicates the relative values of this instance and `other`.

## Examples

### Creating a Signed Wrapper

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 1024);

// Create an unsigned buffer representing the absolute value "42"
var unsignedBuffer = GpuUIntBigInteger.FromBigInteger(context, 42);

// Wrap it as a negative number: -42
var signedGpuNum = new GpuBigInteger(unsignedBuffer, isNegative: true);

if (signedGpuNum.IsNegative) 
{
    Console.WriteLine("The number is negative.");
}
```

## Remarks

GSharp uses a **Sign-Magnitude** representation. To avoid Warp Divergence on the GPU, all GPU kernels operate strictly on the unsigned `Value`. For detailed architectural context on why this design was chosen over Two's Complement, see [The Architecture: Signed vs. Unsigned Separation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md#3-the-architecture-signed-vs-unsigned-separation).

## See also



### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigInteger and GpuUIntBigInteger Operators (G#)](Operators.md)
- [GpuUIntBigInteger Struct (G#)](GpuUIntBigInteger.md)

