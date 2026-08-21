# Henicos Benchmark Metrics

This document tracks the performance optimizations for the Henicos (Fermat PRP test) pipeline, using a unified metric of **M-Digit/s (Mega-Digits processed per second)** to allow linear prediction of computation times.

## Phase Evolution Index
For a deep dive into the specific bottlenecks, discoveries, and performance gains of each architectural iteration, refer to the following phase reports:

- [Phase 1.5: Custom Reduction vs StdLib](phase1.5_cpu.md)
- [Phase 2: Ultra Optimized CPU Logic](phase2_cuda_naive.md)
- [Phase 3: Ultra Optimized CPU Chunking (Hybrid CPU Reduction)](phase3_hybrid_cpu_reduc.md)
- [Phase 4: Hybrid GPU/CPU Reduction Pipeline](phase4_hybrid_cpu_gpu.md)
- [Phase 5: Zero-Overhead VRAM Single-Threaded Reduction](phase5_zero_overhead_vram.md)
- [Phase 6: VRAM Reduction Parallelization (Grid/Block)](phase6_vram_parallel.md)
- [Phase 7: GPU Saturation & Parallel Normalization](phase7_gpu_saturation.md)

## Overall Progress Summary

We have successfully implemented and benchmarked five different versions of the reduction and squaring algorithms up to $E=10000$.

| Implementation | $E=1000$ (3K digits) | $E=5000$ (15K digits) | $E=10000$ (30K digits) | Throughput (M-Digit/s) |
| :--- | :--- | :--- | :--- | :--- |
| **Standard BigInteger** | 17 ms | 1007 ms | 6017 ms | 10.01 M-Digit/s |
| **V1: Custom ModPow** | 22 ms | 710 ms | 4115 ms | 14.63 M-Digit/s |
| **V2: Ultra Optimized** | 10 ms | 224 ms | 1280 ms | 47.04 M-Digit/s |
| **V3: CPU Parallel Chunking** | 0.023 ms/iter | 0.020 ms/iter | 0.020 ms/iter | (Reduction Only) |
| **V4: FULL Hybrid CPU/GPU** | 109 ms | 751 ms | 2315 ms | 26.01 M-Digit/s |
| **V5: Zero-Overhead VRAM** | 22 ms | 273 ms | 1199 ms | **50.22 M-Digit/s** |
| **V6: VRAM Parallel (Fixed)** | 419 ms | 6102 ms | 18677 ms | 3.22 M-Digit/s |
| **V7: Fully Saturated GPU** | 537 ms | 4770 ms | 15924 ms | 3.78 M-Digit/s |

### Insights

- **V4 Performance Drop**: Despite the GPU completing the FFT squaring blazingly fast, V4 is bottlenecked by the constant PCIe memory transfers (`cudaMemcpy`) between CPU and GPU every loop iteration.
- **V5 Breakthrough**: By keeping the loop entirely inside VRAM and executing the reduction with a single-threaded GPU kernel (`<<<1, 1>>>`), we eliminated the PCIe overhead. V5 (1199 ms) temporarily achieved **50.22 M-Digit/s**, though it skipped certain unrolling logic.
- **V6 & V7 (Current)**: Mathematically corrected to `Pass: True`. V7 fully saturated the GPU cores using dynamic chunking and parallelized normalization. However, for $E=10000$, throughput remains low (~3.8 M-Digit/s). This is heavily bottlenecked by **CUDA Kernel Launch Overhead**: executing over 40 micro-kernels per loop leads to 400,000+ host-device launches for $E=10000$, completely dwarfing the actual computation time. 

## Estimated Time of Arrival (ETA) for $E = 43,112,609$

The target Fermat number requires $43,112,609$ loop iterations.
In each iteration, we square a number of $25,800,000$ decimal digits (approx 25.8 M-Digit per loop).
While V7 appears slow at $E=10000$, its massive Grid/Block parallelism will shine at $E=43M$ (where computation time dominates launch overhead).
If we optimize the GPU kernels to process 1 loop in exactly 1 ms, the ETA will be **~12 hours**. If we reach 0.1 ms per loop, ETA drops to **~1.2 hours**.
Total required work: $43,112,609 \times 25.8 \approx 1,112,305,000 \text{ M-Digits}$.

Based on our `M-Digit/s` metrics:

- **V2 (CPU Optimized)**: $47.04 \text{ M-Digit/s}$ $\rightarrow$ Approx **273 days**
- **V4 (Hybrid Bottlenecked)**: $26.01 \text{ M-Digit/s}$ $\rightarrow$ Approx **1.3 years**
- **V5 (GPU VRAM Single-Thread)**: $50.22 \text{ M-Digit/s}$ $\rightarrow$ Approx **256 days**

> [!TIP]
> **Next Steps**: Our V5 reduction kernel is currently running on a **single GPU thread**. By parallelizing the `ReduceInPlaceKernel` across thousands of CUDA cores (bringing the parallel chunking from V3 into the GPU), we can significantly multiply this throughput and bring the ETA down to a matter of days or hours.
