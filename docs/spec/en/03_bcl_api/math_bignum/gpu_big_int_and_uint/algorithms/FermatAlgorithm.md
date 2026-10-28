---
title: "FermatAlgorithm Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Fermat probable-prime test of N = 2^(2E+1) - 2^E + 11 on the GPU, with the Gerbicz-Li error check and checkpoints."
---

## FermatAlgorithm Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Runs the Fermat probable-prime (PRP) test of `N(E) = 2^(2E+1) - 2^E + 11 = 2X^2 - X + 11` (`X = 2^E`), the number G# was started for (`E = 71428571`, about 43 million digits).

```csharp
public sealed class FermatAlgorithm : IDisposable
```

> [!NOTE]
> The class tests `N(E)`, not the Fermat numbers `F_E = 2^(2^E) + 1`; "Fermat" refers to Fermat's little theorem. Earlier versions of this page described Fermat numbers by mistake (#532).

## What the test decides

`Run` decides whether `a^(N-1) = 1 (mod N)` for the base `a` (default 3):

- If not, `N` is certainly composite.
- If so, `N` is a probable prime to base `a`. This is not a proof: most of `N - 1` cannot be factored, so Pocklington or Proth certificates do not apply.
- If `gcd(a, N) != 1`, the result reports that divisor (`SmallDivisor`) without any squaring. For `a = 3` this is every even `E`, because then `2^E = 1 (mod 3)` and `N = 0 (mod 3)`.

From `N - 1 = 2^(2E+1) - 2^E + 10`, and because `a` is invertible modulo `N`:

```text
a^(N-1) = 1  <=>  u_K * a^10 = u_E   (mod N),   u_0 = a,  u_(k+1) = u_k^2,  K = 2E + 1
```

So the test is a chain of `2E + 1` squarings and nothing else (the same shape as gpuowl and PRST), and the verdict compares two residues. `Residue64` and `CheckResidue64` are the low 64 bits of `u_K * a^10 mod N` and `u_E mod N`; they are equal exactly when `N` is a probable prime.

## Error checking (Gerbicz-Li)

The whole chain runs under the Gerbicz-Li check. With the block size `B`, a product `d` of the chain values at every block boundary is kept, and a check compares `d_old^(2^B) * u_0` with `d_old * u_k`. Checks run every `CheckIntervalBlocks` blocks (by default `B` blocks, that is `B^2` squarings) and at the end. The chain is extended to the next block boundary (`K' = roundUp(K, B)`), so that every value read out of it is followed by a check. A failed check rolls back to the last verified point; `MaxConsecutiveRollbacks` failures in a row throw `InvalidOperationException`.

The check covers the computation, not copies kept outside the chain (the saved `u_E`, a checkpoint in a file). A checkpoint damaged in storage is detected by the checks after it is restored, which then fail every time.

## Stages

| `E` | Where it runs | How |
| --- | --- | --- |
| 1 to 31 (`N < 2^64`) | Host | `UInt128` products, no GPU memory. |
| 32 to `MaxExponent` | GPU | NTT squaring with buffers the instance owns, and the fold `QuadraticFoldReduceKernel` (three passes of `r + q * (2^E - 11)`, see [GpuBigIntFermatKernels](../kernels/GpuBigIntFermatKernels.md)), replayed as CUDA Graphs. |
| 32 to `MaxExponent`, CPU fallback | Host | `BigInteger` products and the same fold on the host; the same results as the GPU. |

`MaxExponent = 134217727` (`2^27 - 1`): the square of a residue then fills the longest product the NTT multiplication supports (`2^24` words). For `E >= 32`, the CPU fallback mode in effect at construction is kept for the instance's lifetime; calling a member after `GpuKernelLoader.ForceCpuFallback` changed it throws `InvalidOperationException`, `Dispose` included (which then releases nothing).

## Constructors

| Name | Description |
| --- | --- |
| [FermatAlgorithm(int, uint, FermatPrpOptions?)](#fermatalgorithmint-uint-fermatprpoptions) | Initializes a test of `N(E)` to base `a`. |

## FermatAlgorithm(int, uint, FermatPrpOptions?)

```csharp
public FermatAlgorithm(int e, uint @base = 3, FermatPrpOptions? options = null);
```

#### Parameters

`e` _int_<br>
The exponent `E`, from 1 to `MaxExponent`.

`base` _uint_<br>
The base `a` (default 3, as in gpuowl and PRST).

`options` _FermatPrpOptions?_<br>
The Gerbicz-Li options, or `null` for the defaults.

#### Exceptions

`ArgumentOutOfRangeException`<br>
`e` is out of range; `base` is less than 2, or is 0, 1 or `N - 1` modulo `N` (possible only for small `E`); `BlockSize` or `CheckIntervalBlocks` is negative, `BlockSize` exceeds `2E + 1`, or `MaxConsecutiveRollbacks` is less than 1. Checked before any allocation.

`InvalidOperationException`<br>
GPU context initialization fails.

## Fields and properties

| Name | Description |
| --- | --- |
| `MaxExponent` | The largest exponent, `2^27 - 1`. |
| `E` | The exponent `E`. |
| `Base` | The base `a`. |
| `ModulusBits` | `2E + 1`, the bit position of the leading term of `N`. |
| `ResidueWords` | The word count of a residue: `ceil((2E + 1) / 32)`. |
| `ChainLength` | The squarings of the test, `2E + 1`. |
| `BlockSize` | The Gerbicz-Li block size in use (the option, or `clamp(floor(sqrt(2E + 1)), 2, 1000)`). |
| `Iteration` | The squarings done on the state: the position of the last run (or of a restore) plus the squarings of `RunBenchmarkChunk` since. |

## Methods

| Name | Description |
| --- | --- |
| `Run()` | Runs the test and returns a `FermatPrpResult`. The chain starts again from `u_0 = a`, unless `Restore` was called since the last run. |
| `RunAsync(IProgress<FermatPrpProgress>?, CancellationToken)` | Like `Run`, waiting for the GPU without blocking the thread. Reports the position at every block boundary and checks the token there. On cancellation the state goes back to the last verified point. |
| `CreateCheckpoint()` | Returns the last point that passed a check (the start of the chain before any check). |
| `Restore(FermatPrpCheckpoint)` | Sets the state to a checkpoint, from which the next run continues. The state and `Iteration` change at once (`GetState` returns the checkpoint's state). Throws `ArgumentException` for a checkpoint of another `E`, base or block size, a position off the block grid, a residue of the wrong length or not below `N`, or a negative count. |
| `RunBenchmarkChunk(int)` / `RunBenchmarkChunkAsync(int, CancellationToken)` | Squares the state without checks, for throughput measurement. It advances `Iteration` but is not part of a test. A negative count throws `ArgumentOutOfRangeException`. |
| `GetState()` / `GetStateAsync(CancellationToken)` | Returns the canonical state `u mod N`, `ResidueWords` words. |
| `GetModulus()` | Returns `N` as a `BigInteger` (allocates `O(E)` bits). |
| `Dispose()` | Releases the residues, the scratch buffers, the CUDA Graphs and the workspace. |

Every member throws `ObjectDisposedException` after `Dispose`.

## FermatPrpOptions Record

| Property | Default | Description |
| --- | --- | --- |
| `BlockSize` | 0 (automatic) | The block size `B`, at most `2E + 1`; a check costs `B` extra squarings. |
| `CheckIntervalBlocks` | 0 (`B` blocks) | The blocks between checks. The chain always ends with a check. |
| `MaxConsecutiveRollbacks` | 3 | The failed checks in a row that end the run with an exception. |
| `OnVerifiedCheckpoint` | `null` | Receives every point that passed a check, for example to save it to a file. |

## FermatPrpResult Struct

| Field | Description |
| --- | --- |
| `IsProbablePrime` | `true` when `a^(N-1) = 1 (mod N)`. |
| `SmallDivisor` | `gcd(a, N)` when it is not 1. No squaring was done then: `Residue64`, `CheckResidue64`, `Iterations`, `ChecksPassed` and `Rollbacks` are 0. |
| `Residue64` | The low 64 bits of `u_K * a^10 mod N`. |
| `CheckResidue64` | The low 64 bits of `u_E mod N`. |
| `Iterations` | The squarings of the chain actually run, including those repeated after rollbacks. |
| `ChecksPassed`, `Rollbacks` | The Gerbicz-Li checks passed and the rollbacks. |
| `Elapsed` | The time the run took. |

## FermatPrpCheckpoint Class

An immutable point of the chain that passed a check: `E`, `Base`, `BlockSize`, `Iteration` (a multiple of the block size), the canonical residues `State` (`u_k`), `Check` (`d`), `SnapshotE` (`u_E`, empty when `Iteration <= E`) and `FinalK` (`u_K`, empty when `Iteration <= K`; the last point, at `K' > K`, carries it so that a run resumed from there decides again without squaring), `ChecksPassed` and `Rollbacks`. The public constructor rebuilds one from a saved file.

## Examples

### Testing N(E) for Probable Primality

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;

// Execution Context: Host (CPU); E = 61 and E = 33 run on the GPU, or on the host in CPU fallback mode.
foreach (var e in new[] { 31, 61, 33 })
{
    using var test = new FermatAlgorithm(e);
    var result = test.Run();
    Console.WriteLine($"E={e}: probable prime {result.IsProbablePrime}, residue {result.Residue64:X16}, checks {result.ChecksPassed}");
}

// E = 31 and E = 61 give a probable prime, E = 33 a composite N.
```

### Resuming From a Checkpoint

```csharp
var saved = new List<FermatPrpCheckpoint>();
using (var first = new FermatAlgorithm(61, 3, new FermatPrpOptions { BlockSize = 7, OnVerifiedCheckpoint = saved.Add }))
{
    first.Run();
}

using var resumed = new FermatAlgorithm(61, 3, new FermatPrpOptions { BlockSize = 7 });
resumed.Restore(saved[1]);   // continues from iteration 98
var result = resumed.Run();
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GpuBigUInt Class](../GpuUIntBigInteger.md)
- [GpuBigIntFermatKernels Class](../kernels/GpuBigIntFermatKernels.md)
- [FermatRunner (renamed, migration notes)](Fermat.md)
