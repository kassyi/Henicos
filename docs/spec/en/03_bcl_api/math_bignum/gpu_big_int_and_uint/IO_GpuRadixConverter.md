---
title: "GpuRadixConverter Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for converting massive integers to Base-10 strings efficiently."
---

## GpuRadixConverter Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Converting a multi-megabyte integer from binary (Base-2) into a decimal string (Base-10) is computationally brutal. Traditional $O(N^2)$ division algorithms take days or years to convert numbers with millions of digits.

`GpuRadixConverter` implements a **Divide-and-Conquer Base Conversion** algorithm using the Fast Fourier Transform (NTT). By recursively dividing the massive number into upper and lower halves, it reduces the string conversion time complexity to $O(N \log^2 N)$, making it possible to print millions of digits in seconds.

## Methods

| Name                                                                                                                                       | Description                                                                |
| ------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------- |
| [ConvertAndWrite(GpuContext, GpuUIntBigInteger, int, TextWriter, bool)](#convertandwrite-gpucontext-gpuuintbiginteger-int-textwriter-bool) | Converts a massive GpuUIntBigInteger to Base-10 and writes it to a stream. |

## ConvertAndWrite(GpuContext, GpuUIntBigInteger, int, TextWriter, bool)

Executes the divide-and-conquer base conversion algorithm across the GPU and streams the resulting text directly to the provided writer.

```csharp
public static void ConvertAndWrite(
    GpuContext context,
    GpuUIntBigInteger value,
    int targetDigits,
    TextWriter writer,
    bool isMostSignificant = false);
```

#### Parameters

`context` _GpuContext_<br>
The GPU computation context.

`value` _GpuUIntBigInteger_<br>
The massive integer to convert.

`targetDigits` _int_<br>
The number of digits to generate.

`writer` _TextWriter_<br>
The output stream writer.

`isMostSignificant` _bool_<br>
True if formatting the integer part with a decimal point (e.g., "3.1415...").

#### Returns

_void_

## Examples

### Printing a 10-Million Digit Number

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 524288);

// Assume we just finished calculating a massive factorial or Pi
GpuUIntBigInteger resultGpu = CalculateMassiveFactorial(context);

Console.WriteLine("Starting base-10 conversion (this might take a few seconds)...");

// The converter orchestrates thousands of recursive division kernels on the GPU
// and streams the result directly to a file without allocating a massive string.
using (var writer = new StreamWriter("result.txt"))
{
    GpuRadixConverter.ConvertAndWrite(context, resultGpu, targetDigits: 10_000_000, writer);
}

Console.WriteLine("Conversion complete. Output written to result.txt.");
```

## Remarks

Because the conversion involves hundreds of recursive Division and Remainder (`DivRem`) operations, it requires a significant amount of temporary memory. The converter automatically utilizes the `GpuContext` memory pool.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigInteger Struct (G#)](GpuBigInteger.md)
