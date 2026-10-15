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

- `new FermatRunner(e)` becomes `new FermatAlgorithm(e)`.
- `RunFermatPrp()` returns the final state and the elapsed time as `(uint[] Result, long ElapsedMs)`, not a `bool`. The caller compares the result against the expected residue; there is no built-in primality verdict.
- Non-blocking variants (`RunFermatPrpAsync`, `RunBenchmarkChunkAsync`, `GetStateAsync`) are available.

See [FermatAlgorithm Class](FermatAlgorithm.md) for the full reference.

## See also

### BCL API > Math Bignum

- [FermatAlgorithm Class](FermatAlgorithm.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
