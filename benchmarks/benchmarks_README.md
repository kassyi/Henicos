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
- [Phase 8: Six-Step FFT Architecture (Grid Sync Elimination)](phase8_six_step_fft.md)

## Overall Progress Summary

We have successfully implemented and benchmarked various versions of the reduction and squaring algorithms up to $E=10000$ and $E=20000$.

| Implementation | $E=1000$ (3K digits) | $E=5000$ (15K digits) | $E=10000$ (30K digits) | $E=20000$ (60K digits) | Throughput |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Standard BigInteger** | 16 ms | 1031 ms | 6404 ms | 38519 ms | ~9.40 M-Digit/s |
| **V1: Custom ModPow** | 22 ms | 710 ms | 4115 ms | N/A | 14.63 M-Digit/s |
| **V2: Ultra Optimized** | 10 ms | 224 ms | 1280 ms | N/A | 47.04 M-Digit/s |
| **V3: CPU Parallel Chunking** | 0.023 ms/iter | 0.020 ms/iter | 0.020 ms/iter | N/A | (Reduction Only) |
| **V4: FULL Hybrid CPU/GPU** | 109 ms | 751 ms | 2315 ms | N/A | 26.01 M-Digit/s |
| **V5-V7: Cooley-Tukey (Grid Bound)** | 537 ms | 4770 ms | 15924 ms | N/A | 3.78 M-Digit/s |
| **Phase 8: Six-Step FFT (Current)** | 565 ms | 3281 ms | 6883 ms | **15767 ms** | **~15.27 M-Digit/s** |

### Insights

- **V4 Performance Drop**: Bottlenecked by the constant PCIe memory transfers (`cudaMemcpy`) between CPU and GPU every loop iteration.
- **V6 & V7 Grid Launch Bottleneck**: Mathematically correct, but heavily bottlenecked by **CUDA Kernel Launch Overhead**. Executing over 40 micro-kernels per loop leads to 400,000+ host-device launches for $E=10000$.
- **Phase 8 Breakthrough**: Implemented a **Six-Step FFT Architecture**, decomposing the 1D NTT into 2D matrices and doing Row NTTs in shared memory. This eliminated grid-sync barriers entirely, achieving **2.44x speedup over CPU for $E=20000$**.

## Estimated Time of Arrival (ETA) for $E = 43,112,609$

The target Fermat number requires $43,112,609$ loop iterations.
In each iteration, we square a number of $25,800,000$ decimal digits (approx 25.8 M-Digit per loop).
If we optimize the GPU kernels to process 1 loop in exactly 1 ms, the ETA will be **~12 hours**. If we reach 0.1 ms per loop, ETA drops to **~1.2 hours**.
Total required work: $43,112,609 \times 25.8 \approx 1,112,305,000 \text{ M-Digits}$.

Based on our `M-Digit/s` metrics:

- **V2 (CPU Optimized)**: $47.04 \text{ M-Digit/s}$ $\rightarrow$ Approx **273 days**
- **V4 (Hybrid Bottlenecked)**: $26.01 \text{ M-Digit/s}$ $\rightarrow$ Approx **1.3 years**
- **Phase 8 (GPU VRAM)**: $\sim 15.27 \text{ M-Digit/s}$ (at $E=20000$) $\rightarrow$ Approx **843 days** (However, this throughput continues to rapidly scale as GPU utilization increases!)

> [!TIP]
> **Next Steps**: Our Phase 8 Six-Step FFT currently achieves ~15.27 M-Digit/s at $E=20000$, but the GPU is still idling ($\sim 2048$ threads). By moving the primary `for(i=0; i<E; i++)` loop entirely into a single persistent GPU kernel (**Static Device-Side Execution**), we will eliminate the `cudaGraphLaunch` host-loop overhead and fully saturate the GPU, multiplying this throughput and driving the ETA down massively.
