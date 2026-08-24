# Phase 4: Full Hybrid CPU/GPU Pipeline (V4)

## Objective
To overcome the $O(N^2)$ scaling limitation of the CPU-only approach (V2) by offloading the multiplication (squaring) to the GPU using `cuFFT`, which operates at $O(N \log N)$ complexity. The reduction step was still handled on the CPU utilizing a multi-threaded parallel chunking algorithm (Phase 3).

## Architecture
- **Multiplication**: Handled by GPU using Radix-16 encoding and `cuFFT` for massive number multiplication.
- **Reduction**: Handled by CPU using `HybridReduction.cs`, breaking the array into chunks and processing them in parallel.
- **Data Transfer**: Every iteration of the Fermat PRP loop required transferring the data array back and forth between CPU and GPU RAM over the PCIe bus (`cudaMemcpy`).

## Benchmark Results
Metric: Mega-Digits processed per second (M-Digit/s)

| E | Digits (Approx) | V2 (CPU Only) | V4 (Hybrid CPU/GPU) |
|---|---|---|---|
| 500 | 1,500 | 150.67 | 1.86 |
| 1000 | 3,000 | 75.30 | 3.67 |
| 2000 | 6,000 | 56.02 | 6.21 |
| 5000 | 15,000 | 67.81 | 7.85 |
| 10000 | 30,000 | 47.22 | 8.45 |

*(Note: V2 data slightly varied across runs, but the trend remains clear.)*

## Insights & Analysis
1. **The PCIe Bottleneck**: Although the GPU completes the FFT squaring operations blazingly fast, the pipeline is severely bottlenecked by the constant `cudaMemcpy` operations. The time spent sending the data over the PCIe bus far exceeded the computation time saved by the GPU.
2. **Scaling Potential**: Notice how V2's performance drops as $E$ scales (from 150.67 to 47.22 M-Digit/s), while V4's performance *increases* as $E$ scales (from 1.86 to 8.45 M-Digit/s). This proves that the $O(N \log N)$ FFT approach is fundamentally correct for gigantic numbers, and V4 would eventually overtake V2 at extremely large values of $E$.
3. **Conclusion for $E=43M$**: While V4 scales better, its absolute throughput at our measurable ranges is still too low due to memory transfer latency. At ~26.01 M-Digit/s (observed in later runs), the ETA for $E=43,112,609$ was still around **1.3 years**.

## Next Steps
To unlock the true power of the GPU, we must eliminate the PCIe bottleneck. This requires rewriting the reduction logic entirely as a CUDA kernel so that the data never leaves the GPU's VRAM during the millions of loop iterations. This leads us to Phase 5.
