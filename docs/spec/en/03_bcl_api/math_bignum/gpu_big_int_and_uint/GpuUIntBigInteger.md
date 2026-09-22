---
title: "GpuUIntBigInteger Struct (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for the core unsigned GPU memory buffer for massive integers."
---

## GpuUIntBigInteger Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents an unsigned, arbitrary-precision integer buffer residing directly in GPU VRAM.

This is the absolute core foundation of all `Stdlib.Math` operations in GSharp. Every advanced algorithm (NTT, Fermat, Chudnovsky) ultimately executes its CUDA kernels against arrays of `GpuUIntBigInteger`.

## Methods

| Name | Description |
| --- | --- |
| [FromBigInteger(GpuContext, BigInteger)](#frombiginteger-gpucontext-biginteger) | Allocates a GPU buffer from a .NET `BigInteger`. |
| [FromByteArray(GpuContext, byte[])](#frombytearray-gpucontext-byte) | Allocates a GPU buffer from a raw byte array. |
| [ToBigInteger()](#tobiginteger) | Downloads the buffer to the CPU and converts it to a `BigInteger`. |
| [ToByteArray()](#tobytearray) | Downloads the buffer to the CPU as a raw byte array. |

## FromBigInteger(GpuContext, BigInteger)

Allocates a new unsigned multi-word buffer in GPU VRAM and uploads the absolute value of the provided `BigInteger`.

```csharp
public static GpuUIntBigInteger FromBigInteger(GpuContext context, BigInteger value);
```

#### Parameters

`context` _GpuContext_<br>
The context pool that defines the maximum bit-length capacity.

`value` _BigInteger_<br>
The source .NET `BigInteger`. (If negative, its absolute value is taken).

#### Returns
_GpuUIntBigInteger_<br>
The allocated GPU buffer.

## FromByteArray(GpuContext, byte[])

Allocates a new GPU buffer from a little-endian byte array.

```csharp
public static GpuUIntBigInteger FromByteArray(GpuContext context, byte[] data);
```

#### Parameters

`context` _GpuContext_<br>
The context pool.

`data` _byte[]_<br>
The raw byte array (Little-Endian).

#### Returns
_GpuUIntBigInteger_<br>
The allocated GPU buffer.

## ToBigInteger()

Synchronously downloads the VRAM buffer to Host memory and constructs a .NET `BigInteger`.

```csharp
public BigInteger ToBigInteger();
```

#### Returns
_BigInteger_<br>
The resulting .NET `BigInteger`.

## ToByteArray()

Synchronously downloads the VRAM buffer to Host memory as a little-endian byte array.

```csharp
public byte[] ToByteArray();
```

#### Returns
_byte[]_<br>
The raw byte array.

## Examples

### Moving Data between CPU and GPU

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 1024);

// 1. Upload to GPU
var dotNetBigInt = BigInteger.Parse("340282366920938463463374607431768211455");
using var gpuBuffer = GpuUIntBigInteger.FromBigInteger(context, dotNetBigInt);

// 2. Do massive GPU calculations...
// ...

// 3. Download back to CPU
BigInteger result = gpuBuffer.ToBigInteger();
```

## Remarks

Unlike `GpuBigInteger`, `GpuUIntBigInteger` contains no sign information. It represents pure magnitude. For more architectural context, see [The Architecture: Signed vs. Unsigned Separation](../../04_guides/07_stdlib_math_and_lazy_evaluation.md#3-the-architecture-signed-vs-unsigned-separation).

## See also

### Related Integer Types
- [GpuBigInteger](GpuBigInteger.md)
- [Operators](Operators.md)
- [IO_GpuRadixConverter](IO_GpuRadixConverter.md)
- [GpuBigInteger and GpuUIntBigInteger Overview](00_overview.md)

### Algorithms
- [DivRem](algorithms/DivRem.md)
- [Gcd](algorithms/Gcd.md)
- [ModPow](algorithms/ModPow.md)
- [Ntt](algorithms/Ntt.md)

### Architecture & Context
- [GpuContext](../gpu_context/GpuContext.md)
- [Arbitrary-Precision Math Overview](../00_overview.md)
- [Gpu BCL API Overview](../../00_overview.md)
- [4. Bignum Transpiler Internals](../../../02_architecture/04_bignum_internals.md)

### Guides
- [Stdlib Math and Lazy Evaluation Guide](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
