---
title: "GpuRadixConverter Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Provides high-performance GPU-accelerated Divide and Conquer (D&C) radix conversion for streaming arbitrary-precision GpuUIntBigInteger to decimal text."
---

## GpuRadixConverter Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.IO`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Provides high-performance GPU-accelerated Divide and Conquer (D&C) radix conversion for streaming arbitrary-precision `GpuUIntBigInteger` to decimal text.

```csharp
public static class GpuRadixConverter
```

## Methods

| Name | Description |
| --- | --- |
| [ConvertAndWrite(GpuContext, GpuUIntBigInteger, int, TextWriter, bool)](#convertandwritegpucontext-gpuuintbiginteger-int-textwriter-bool) | Converts a `GpuUIntBigInteger` to decimal digits using GPU-accelerated divide-and-conquer division, streaming the output directly to a `TextWriter`. |

## ConvertAndWrite(GpuContext, GpuUIntBigInteger, int, TextWriter, bool)

Converts a `GpuUIntBigInteger` to decimal digits using GPU-accelerated divide-and-conquer division, streaming the output directly to a `TextWriter`.

```csharp
public static void ConvertAndWrite(GpuContext context, GpuUIntBigInteger value, int targetDigits, TextWriter writer, bool isMostSignificant = false);
```

#### Parameters

`context` _GpuContext_<br>
The GPU computation context.

`value` _GpuUIntBigInteger_<br>
The integer to convert.

`targetDigits` _int_<br>
The number of digits to generate.

`writer` _TextWriter_<br>
The output stream writer.

`isMostSignificant` _bool_<br>
True if formatting the integer part with a decimal point (e.g. "3.1415...").

#### Exceptions

`ArgumentNullException`<br>
Thrown when `context`, `value`, or `writer` is `null`.

`ArgumentOutOfRangeException`<br>
Thrown when `targetDigits` is negative or zero.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.Math

### Printing a 10,000 Digit Number

```csharp
// Execution Context: Host (CPU)
using System;
using System.IO;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.IO;

using var context = new GpuContext(2048, GpuExecutionEngine.GSharpAot);

// Assume we just finished calculating a massive factorial or Pi
GpuUIntBigInteger resultGpu = new GpuUIntBigInteger(context, 100);

Console.WriteLine("Starting base-10 conversion (this might take a few seconds)...");

// The converter orchestrates thousands of recursive division kernels on the GPU
// and streams the result directly to a file without allocating a massive string.
using (var writer = new StreamWriter("result.txt"))
{
    GpuRadixConverter.ConvertAndWrite(context, resultGpu, targetDigits: 10_000, writer);
}

Console.WriteLine("Conversion complete. Output written to result.txt.");
```

## Remarks

Because the conversion involves hundreds of recursive Division and Remainder (`DivRem`) operations, it requires a significant amount of temporary memory. The converter automatically utilizes the `GpuContext` memory pool.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuContext](../../../03_bcl_api/math_bignum/gpu_context/GpuContext.md)
- [GpuUIntBigInteger](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuUIntBigInteger.md)
