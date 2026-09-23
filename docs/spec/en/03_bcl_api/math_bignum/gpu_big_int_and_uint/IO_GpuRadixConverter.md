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

| Name | Description |
| --- | --- |
| [ToString(GpuBigInteger, int)](#tostring-gpubiginteger-int) | Converts a massive GpuBigInteger to a Base-10 string. |

## ToString(GpuBigInteger, int)

Executes the divide-and-conquer base conversion algorithm across the GPU and returns the resulting string to the Host.

```csharp
public static string ToString(GpuBigInteger value, int radix = 10);
```

#### Parameters

`value` _GpuBigInteger_<br>
The massive integer to convert.

`radix` _int_<br>
The numerical base to convert to (currently optimized and restricted to 10).

#### Returns
_string_<br>
A massive string representing the number in Base-10.

## Examples

### Printing a 10-Million Digit Number

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 524288);

// Assume we just finished calculating a massive factorial or Pi
var resultGpu = CalculateMassiveFactorial(context);

Console.WriteLine("Starting base-10 conversion (this might take a few seconds)...");

// The converter orchestrates thousands of recursive division kernels on the GPU
string decimalString = GpuRadixConverter.ToString(resultGpu, radix: 10);

Console.WriteLine($"Length of string: {decimalString.Length} characters.");
File.WriteAllText("result.txt", decimalString);
```

## Remarks

Because the conversion involves hundreds of recursive Division and Remainder (`DivRem`) operations, it requires a significant amount of temporary memory. The converter automatically utilizes the `GpuContext` memory pool.

## See also



### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
- [GpuBigInteger Struct (G#)](GpuBigInteger.md)

