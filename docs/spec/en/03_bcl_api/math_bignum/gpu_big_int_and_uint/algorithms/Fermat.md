---
title: "FermatRunner Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for Fermat primality testing and factorization on the GPU."
---

## FermatRunner Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Provides algorithms based on Fermat's Little Theorem. While probabilistic primality tests (like Miller-Rabin) are common, Fermat factorization is highly effective when a composite number is the product of two primes that are close to each other.

Because Fermat factorization requires searching for integer square roots across a massive search space ($x^2 - N = y^2$), it is highly parallelizable and perfectly suited for the GPU.

## Constructors

| Name | Description |
| --- | --- |
| [FermatRunner(GpuContext)](#fermatrunner-gpucontext) | Initializes a new instance of the FermatRunner using the specified context. |

## FermatRunner(GpuContext)

Initializes a new instance of the FermatRunner using the specified context.

```csharp
public FermatRunner(GpuContext context);
```

#### Parameters

`context` _GpuContext_<br>
The context memory pool to use for dynamic search allocations.

## Methods

| Name | Description |
| --- | --- |
| [TryFactor(GpuBigInteger, out GpuBigInteger, out GpuBigInteger)](#tryfactor-gpubiginteger-out-gpubiginteger-out-gpubiginteger) | Attempts to factor a composite number using Fermat's method. |

## TryFactor(GpuBigInteger, out GpuBigInteger, out GpuBigInteger)

Searches for factors using thousands of concurrent GPU threads. The runner will automatically chunk the search space and dispatch kernels until it finds a valid $y$ such that $x^2 - N = y^2$.

```csharp
public bool TryFactor(GpuBigInteger n, out GpuBigInteger factorP, out GpuBigInteger factorQ);
```

#### Parameters

`n` _GpuBigInteger_<br>
The composite number to factor.

`factorP` _out GpuBigInteger_<br>
When this method returns, contains the first prime factor if found; otherwise, an uninitialized value.

`factorQ` _out GpuBigInteger_<br>
When this method returns, contains the second prime factor if found; otherwise, an uninitialized value.

#### Returns

_bool_<br>
`true` if factors were successfully found within the search limit; otherwise, `false`.

## Examples

### Factoring a Composite Number via Parallel Search

This example demonstrates how to orchestrate a brute-force Fermat search across the GPU, where each thread checks a different $x$ offset simultaneously.

```csharp
// Execution Context: Host (CPU)

using var context = new GpuContext(maxWords: 128); // 4096-bit composite number

// The massive composite number we want to factor
var compositeN = BigInteger.Parse("... massive number ...");
using var gpuN = GpuUIntBigInteger.FromBigInteger(context, compositeN);

// Initialize the runner
var runner = new FermatRunner(context);

// Dispatch the GPU kernels
bool found = runner.TryFactor(new GpuBigInteger(gpuN), out GpuBigInteger factorP, out GpuBigInteger factorQ);

if (found)
{
    Console.WriteLine($"Found prime factor P: {factorP.ToBigInteger()}");
    Console.WriteLine($"Found prime factor Q: {factorQ.ToBigInteger()}");
    
    // Clean up the dynamically allocated results
    factorP.Dispose();
    factorQ.Dispose();
}
else
{
    Console.WriteLine("No factors found within the search limit.");
}
```

## Remarks

Instead of returning a simple AST node, Fermat algorithms in GSharp use the `FermatRunner` orchestrator to iteratively dispatch threads and manage GPU memory lifecycle safely across multiple passes.

## See also

### Related Algorithms
- [ModPow](ModPow.md)
- [DivRem](DivRem.md)
- [Gcd](Gcd.md)

### Integer Types & Architecture
- [GpuBigInteger](../GpuBigInteger.md)
- [GpuUIntBigInteger](../GpuUIntBigInteger.md)
- [GpuBigInteger and GpuUIntBigInteger Overview](../00_overview.md)
- [GpuContext](../../gpu_context/GpuContext.md)
- [Arbitrary-Precision Math Overview](../../00_overview.md)
- [Gpu BCL API Overview](../../../00_overview.md)

### Guides
- [Stdlib Math and Lazy Evaluation Guide](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [Precision and Performance Guide](../../../../04_guides/05_precision_and_performance_guide.md)
