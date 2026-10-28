---
title: "FermatRunner Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "FermatRunner was renamed to FermatAlgorithm."
---

## FermatRunner Class

> [!IMPORTANT]
> **`FermatRunner` has been renamed to [`FermatAlgorithm`](FermatAlgorithm.md)** (pull request #288, `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms` namespace). No compatibility wrapper is provided, so code using the old name must be updated.

Migration notes:

- `new FermatRunner(e)` becomes `new FermatAlgorithm(e)`. The default base is now 3 (it was 2); pass `new FermatAlgorithm(e, 2)` for the old base.
- The test number is `N = 2^(2E+1) - 2^E + 11`, not the Fermat number `2^(2^E) + 1` that the old documentation named (#532).
- `RunFermatPrp()` squared the state only `E` times and returned the raw state. It is replaced by `Run()`, which runs the real test (`2E + 1` squarings) and returns a `FermatPrpResult` with the verdict `IsProbablePrime` and the two 64-bit residues. `RunFermatPrpAsync` becomes `RunAsync`.
- `RunFermatPrpWithGerbiczCheck` is merged into `Run`: the Gerbicz-Li check is always on (`FermatPrpOptions` sets the block size and interval).
- `LoadState(uint[])` becomes `Restore(FermatPrpCheckpoint)`, and `CreateCheckpoint()` (or `FermatPrpOptions.OnVerifiedCheckpoint`) returns checkpoints that passed a check. A saved raw state of the old format cannot be resumed.
- `GetState()` returns the canonical residue `u mod N` in `ResidueWords = ceil((2E + 1) / 32)` words; `OutNumWords` (the old `2L + 16`-word buffer) is removed.
- `RunBenchmarkChunk`, `RunBenchmarkChunkAsync` and `GetStateAsync` remain; a negative count now throws `ArgumentOutOfRangeException`.

See [FermatAlgorithm Class](FermatAlgorithm.md) for the full reference.

## See also

### BCL API > Math Bignum

- [FermatAlgorithm Class](FermatAlgorithm.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
