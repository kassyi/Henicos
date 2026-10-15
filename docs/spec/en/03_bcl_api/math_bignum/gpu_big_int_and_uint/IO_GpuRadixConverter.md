---
title: "GpuRadixConverter Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Provides high-performance GPU-accelerated Divide and Conquer (D&C) radix conversion for streaming arbitrary-precision GpuBigUInt to decimal text."
---

## GpuRadixConverter Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides high-performance GPU-accelerated Divide and Conquer (D&C) radix conversion for streaming arbitrary-precision `GpuBigUInt` to decimal text.

```csharp
public static class GpuRadixConverter
```

## Methods

| Name | Description |
| --- | --- |
| [ConvertAndWrite(GpuWorkspace, GpuBigUInt, int, TextWriter, bool)](#convertandwritegpuworkspace-gpubiguint-int-textwriter-bool) | Converts a `GpuBigUInt` to decimal digits using GPU-accelerated divide-and-conquer division, streaming the output directly to a `TextWriter`. |

## ConvertAndWrite(GpuWorkspace, GpuBigUInt, int, TextWriter, bool)

Converts a `GpuBigUInt` to decimal digits using GPU-accelerated divide-and-conquer division, streaming the output directly to a `TextWriter`.

```csharp
public static void ConvertAndWrite(
    GpuWorkspace workspace,
    GpuBigUInt value,
    int targetDigits,
    TextWriter writer,
    bool isMostSignificant = false);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU computation workspace managing memory and kernel dispatches.

`value` _GpuBigUInt_<br>
The unsigned large integer to convert.

`targetDigits` _int_<br>
The expected or target number of decimal digits to generate.

`writer` _TextWriter_<br>
The output stream writer receiving the formatted decimal text.

`isMostSignificant` _bool_<br>
`true` if formatting the integer part with a decimal point (e.g. `"3.1415..."`); otherwise, `false`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `workspace` or `writer` is `null`.

`ArgumentOutOfRangeException`<br>
Thrown when `targetDigits` is negative or zero.

## Examples

### Streaming Base-10 Digits from GPU to TextWriter

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using System.IO;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(2048, GpuExecutionEngine.GSharpAot);

// Assume we just finished calculating a massive factorial or Pi
using var resultGpu = GpuBigUInt.FromBigInteger(workspace, BigInteger.Parse("12345678901234567890"), minWords: 2);

// Stream the converted base-10 digits directly to a TextWriter
using var writer = new StringWriter();
GpuRadixConverter.ConvertAndWrite(workspace, resultGpu, targetDigits: 20, writer);

Console.WriteLine($"Conversion complete. Output: {writer}");
```

## Remarks

Because the conversion involves recursive Division and Remainder (`DivRem`) operations for large numbers ($> 10,000$ digits), it requires temporary memory. The converter automatically utilizes the `GpuWorkspace` memory pool and caches $10^k$ radix divisors on the GPU.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
- [GpuBigUInt](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuBigUInt.md)
