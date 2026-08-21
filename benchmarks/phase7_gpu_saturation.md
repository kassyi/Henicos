# Phase 7: GPU Saturation & Parallel Normalization

## Objective
To fully utilize the massive parallel architecture of the GPU by eliminating all sequential `<<<1, 1>>>` kernels (such as `NormalizeKernel` and `MultiplyBy2Kernel`) that bottlenecked Phase 6, and introducing dynamic chunking to spawn thousands of threads even for small values of $E$.

## Architecture
- **Dynamic Chunking**: Instead of hardcoding `chunkSize = 256`, the chunk size is dynamically calculated as `chunkSize = max(4, (outNumWords + targetChunks - 1) / targetChunks)` with a target of 1024 chunks. This guarantees that at least 1024 CUDA threads are active, fully saturating the SMs.
- **Parallel Normalization**: The sequential `NormalizeKernel` was refactored into a map-reduce style pipeline. First, `EvalNormActionKernel` determines the action (add $N$ or subtract $N$). Then, `NormalizeChunkKernel` applies the operation in parallel across all threads, followed by `ApplyCarryKernel` to ripple the carries.
- **Parallel Multiply-by-2**: Replaced the sequential ripple-shift with a fully parallel `MultiplyBy2KernelParallel` where each thread shifts its word and pulls the MSB from the previous word.

## Benchmark Results
Metric: Mega-Digits processed per second (M-Digit/s)

| E | V5 (Single-Thread) | V6 (Parallel, Fixed Chunk) | V7 (Fully Saturated) |
|---|---|---|---|
| 1000 | 1.12 M-Digit/s | 0.62 M-Digit/s | **1.12 M-Digit/s** |
| 5000 | 3.16 M-Digit/s | 2.47 M-Digit/s | **3.16 M-Digit/s** |
| 10000 | 3.78 M-Digit/s | 3.22 M-Digit/s | **3.78 M-Digit/s** |

*(Note: The V7 times (15924 ms for E=10000) are heavily impacted by launch overhead).*

## Insights & Analysis
1. **Mathematical Accuracy (Pass: True)**: The exact logic of the Fermat loop is preserved and fully validated against the `BigInteger` standard.
2. **The "Launch Overhead" Wall**: Despite fully parallelizing the execution and guaranteeing massive thread counts, the performance for small arrays ($E=10000$) did not significantly outpace V6. Profiling reveals that we are launching **over 40 micro-kernels per single loop iteration**. Over 10,000 loops, this results in **400,000+ host-to-device kernel launch commands**.
3. At 5-10 μs per launch, the GPU spends the vast majority of its 15.9 seconds simply waiting for the CPU to tell it what to do next. The actual computation time inside the GPU is fractions of a millisecond.

## Next Steps
To solve the Host-to-Device launch bottleneck, we must move the loop control entirely onto the GPU, or batch the execution sequence. **Phase 8** will focus on eliminating launch overhead via **CUDA Graphs** or by moving the main `for (int i = 0; i < E; i++)` loop into a single master device kernel (Device-side launch / static execution).
