---
title: "FermatRunner Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for Fermat primality testing on the GPU."
---

## FermatRunner Class

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Provides highly optimized algorithms for executing Probable Prime (PRP) tests based on Fermat's Little Theorem. The runner is explicitly tuned to handle massive numbers over millions of loop iterations completely within GPU VRAM, bypassing the host CPU to eliminate PCIe transfer bottlenecks.

## Constructors

| Name                                   | Description                                                                                     |
| -------------------------------------- | ----------------------------------------------------------------------------------------------- |
| [FermatRunner(int)](#fermatrunner-int) | Initializes a new instance of the FermatRunner targeting a specific Fermat number exponent `e`. |

## FermatRunner(int)

Initializes a new instance of the FermatRunner targeting a specific exponent `e` for the Fermat number $F_e = 2^{2^e} + 1$ (or variants thereof).

```csharp
public FermatRunner(int e);
```

#### Parameters

`e` _int_<br>
The exponent defining the size and iteration bounds of the target number to test.

## Methods

| Name                            | Description                                                                  |
| ------------------------------- | ---------------------------------------------------------------------------- |
| [RunFermatPrp()](#runfermatprp) | Executes the Probable Prime test natively on the GPU and returns the result. |

## RunFermatPrp()

Dispatches the complete Fermat PRP loop on the GPU and synchronizes with the host once finished.

```csharp
public bool RunFermatPrp();
```

#### Returns

_bool_<br>
`true` if the number passes the Fermat primality test (it is a Probable Prime); otherwise, `false` (it is composite).

## Examples

### Running a Native GPU PRP Test

This example demonstrates how to orchestrate a Fermat test across the GPU. The runner manages its own internal `GpuContext` and executes all iterations internally.

```csharp
// Execution Context: Host (CPU)

// Initialize the runner for a specific exponent (e.g., e = 1000)
var runner = new FermatRunner(1000);

// Dispatch the GPU kernels.
// This will block the CPU until the GPU finishes all millions of loop iterations.
bool isPrime = runner.RunFermatPrp();

if (isPrime)
{
    Console.WriteLine("Target is a Probable Prime (PRP)!");
}
else
{
    Console.WriteLine("Target is definitively composite.");
}
```

## Remarks

Instead of returning a simple AST node, Fermat algorithms in GSharp use the `FermatRunner` orchestrator to iteratively dispatch threads and manage GPU memory lifecycle safely across multiple passes.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [GpuUIntBigInteger Struct (G#)](../GpuUIntBigInteger.md)
