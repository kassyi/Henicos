# Phase 5: Zero-Overhead VRAM Loop (V5)

## Objective
To resolve the PCIe bottleneck discovered in Phase 4 by migrating the modular reduction step (`ReduceInPlace`) directly into a CUDA kernel. This ensures that the array remains exclusively inside the GPU's Video RAM (VRAM) for the entirety of the millions of loop iterations required by the Fermat PRP test.

## Architecture
- **Multiplication**: `cuFFT` on GPU.
- **Reduction**: Handled by custom CUDA kernels (`ReduceInPlaceKernel`, `NormalizeKernel`, `MultiplyBy2Kernel`). 
- **Execution Model**: To prioritize logical correctness and establish a solid baseline, the reduction kernel was initially implemented using a **single GPU thread** (`<<<1, 1>>>`).
- **Data Transfer**: `cudaMemcpy` is called exactly twice: once to load the initial value $X=2$ into VRAM at the start, and once to read the final result at the very end.

## Benchmark Results
Metric: Mega-Digits processed per second (M-Digit/s)

| E | V2 (Ultra Opt CPU) | V4 (Hybrid GPU Bottlenecked) | V5 (Zero-Overhead VRAM) |
|---|---|---|---|
| 1000 | (varies) | 5.53 M-Digit/s | 27.38 M-Digit/s |
| 5000 | (varies) | 20.04 M-Digit/s | 55.14 M-Digit/s |
| 10000 | 47.04 M-Digit/s | 26.01 M-Digit/s | **50.22 M-Digit/s** |

*(Note: Data taken from latest stabilized benchmark run)*

## Insights & Analysis
1. **PCIe Overhead Extinguished**: By eliminating the loop-level PCIe transfers, V5 saw an immediate ~2x to ~5x speedup over V4 across all tested values of $E$.
2. **Surpassing the CPU Limit**: At $E=10000$, V5 (50.22 M-Digit/s) successfully surpassed the absolute limits of our highly optimized CPU-only implementation V2 (47.04 M-Digit/s).
3. **The Single-Thread Revelation**: The most astonishing fact about V5's performance is that the reduction kernel is running on **a single CUDA thread**. It is matching and beating a highly optimized, vectorized CPU core using only a fraction of a single SM (Streaming Multiprocessor) on the GPU.

## ETA Projection for $E=43,112,609$
With V5 achieving ~50.22 M-Digit/s, the estimated time of arrival (ETA) for processing the target M43 equivalent has plummeted from **1.3 years** (V4) down to **~256 days**.

## Next Steps
While 256 days is a massive improvement, it is still not practical for rapid iteration. However, knowing that our current GPU reduction logic is running on a *single thread*, the path forward is clear: **Phase 6 must parallelize the `ReduceInPlaceKernel` across thousands of CUDA cores.** By distributing the chunked reduction logic across the GPU grid, we expect to bring the computation time down from months to a matter of days or even hours.
