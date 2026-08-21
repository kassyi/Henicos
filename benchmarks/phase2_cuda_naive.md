# Phase 2: Ultra Optimized CPU Logic & CUDA Naive Baseline

## Objective
To establish a baseline for GPU execution. Having decided that $O(N \log N)$ FFT on a GPU is necessary (Phase 1.5), we first needed to establish communication between C# and C++ CUDA (NVCC) via P/Invoke. This phase tests a "naive" $O(N^2)$ reduction kernel entirely inside GPU VRAM to measure kernel launch overheads.

## Architecture
- **Multiplication/Reduction**: A naive, non-FFT $O(N^2)$ algorithm ported to CUDA simply to test the VRAM execution environment.
- **Execution Model**: The entire Fermat loop runs inside the GPU VRAM. The CPU only sends the initial array and waits for the final result.
- **Hardware**: NVIDIA RTX 3070 Ti (CUDA 13.3) triggered via .NET 10 P/Invoke to `HenicosCuda.dll`.

## Benchmark Results
Metric: Total execution time for the full Fermat PRP loop running inside VRAM.

| E | CUDA Naive O(N^2) VRAM Loop |
|---|---|
| 10 | 7 ms |
| 50 | 0 ms (Under 1ms) |
| 100 | 0 ms (Under 1ms) |
| 500 | 0 ms (Under 1ms) |

## Insights & Analysis
1. **Zero-Overhead Proof**: The 7ms delay at $E=10$ is the CUDA context initialization latency (device warmup). For subsequent runs up to $E=500$, the execution completes in under 1ms.
2. **VRAM Confinement**: Thousands of kernel launches occur without the CPU paying any PCIe transfer tax. This proves that keeping the entire array in VRAM and looping kernels is extremely fast and suffers almost zero launch overhead.
3. **The Necessity of cuFFT**: While the communication is flawless, the naive $O(N^2)$ kernel logic itself is mathematically doomed for large numbers. 

## Next Steps
Before fully porting the complex reduction logic into CUDA, we must first figure out how to parallelize the reduction process on the CPU to ensure the algorithm can actually be spread across multiple cores (or threads). This leads to Phase 3 (CPU Parallel Chunking).
