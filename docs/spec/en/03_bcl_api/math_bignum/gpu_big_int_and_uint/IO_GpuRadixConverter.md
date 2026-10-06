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
| [ConvertAndWrite(GpuWorkspace, GpuBigUInt, int, TextWriter, bool)](#convertandwritegpuworkspace-gpuuintbiginteger-int-textwriter-bool) | Converts a `GpuBigUInt` to decimal digits using GPU-accelerated divide-and-conquer division, streaming the output directly to a `TextWriter`. |

## ConvertAndWrite(GpuWorkspace, GpuBigUInt, int, TextWriter, bool)

Converts a `GpuBigUInt` to decimal digits using GPU-accelerated divide-and-conquer division, streaming the output directly to a `TextWriter`.

```csharp
public static void ConvertAndWrite(GpuWorkspace workspace, GpuBigUInt value, int targetDigits, TextWriter writer, bool isMostSignificant = false);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU computation workspace.

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
Thrown when `workspace`, `value`, or `writer` is `null`.

`ArgumentOutOfRangeException`<br>
Thrown when `targetDigits` is negative or zero.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

### Printing a 10,000 Digit Number

```csharp
// Execution Context: Host (CPU)
using System;
using System.IO;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

using var workspace = new GpuWorkspace(2048, GpuExecutionEngine.GSharpAot);

// Assume we just finished calculating a massive factorial or Pi
GpuBigUInt resultGpu = GpuBigUInt.FromBigInteger(workspace, System.Numerics.BigInteger.Parse("12345678901234567890"), minWords: 2);

Console.WriteLine("Starting base-10 conversion...");

// The converter orchestrates thousands of recursive division kernels on the GPU
// and streams the result directly to a file without allocating a massive string.
using (var writer = new StreamWriter("result.txt"))
{
    GpuRadixConverter.ConvertAndWrite(workspace, resultGpu, targetDigits: 20, writer);
}

string output = File.ReadAllText("result.txt");
Console.WriteLine($"Conversion complete. Output: {output}");
```

## Remarks

Because the conversion involves hundreds of recursive Division and Remainder (`DivRem`) operations, it requires a significant amount of temporary memory. The converter automatically utilizes the `GpuWorkspace` memory pool.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)
- [GpuBigUInt](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/GpuBigUInt.md)
