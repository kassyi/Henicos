# Phase 3: CPU Parallel Chunking (Reduction Only)

## Objective
To develop a multi-threaded parallel architecture for the modular reduction step. Multi-precision arithmetic is notoriously difficult to parallelize because the "carry" from one word affects the next word. We needed an algorithm that breaks this sequential dependency so we can eventually port it to thousands of GPU cores.

## Architecture
- **Execution Model**: Multi-threaded CPU execution (`Parallel.For` in C#).
- **The "Scan & Apply" Algorithm**:
  1. **Chunking**: Divide the massive number array into chunks, assigning one chunk to each thread.
  2. **Local Computation**: Each thread computes the reduction formula ($R + (Q \ll E) - 11 \cdot Q$) locally, assuming an incoming carry of `0`. It records its final "carry-out" into a `carryBuffer`.
  3. **Prefix Sum (Ripple Carry)**: A single thread sequentially sweeps the `carryBuffer` to calculate the *true* incoming carry for every chunk.
  4. **Apply Carry**: Threads resume and apply their true incoming carry to their respective chunks.

## Benchmark Results
Metric: Execution time (in nanoseconds) via BenchmarkDotNet.

| E | Standard BigInteger | Ultra Optimized V2 (Single Thread) | V3 Custom Reduction V1 |
|---|---|---|---|
| 10 | 56 ns | 1,615 ns | 3,092 ns |
| 500 | 2,145,332 ns | 946,539 ns | 1,948,743 ns |
| 5000 | 1,011,907,333 ns | 221,539,486 ns | 696,285,657 ns |
| 10000 | 5,890,012,235 ns | 1,232,075,253 ns | 4,041,677,150 ns |

*(Note: V3 tests here show the raw algorithm overhead compared to the absolute limit of V2's single-thread linear pass).*

## Insights & Analysis
1. **Parallelization Overhead**: On small values of $E$, creating tasks and synchronizing threads is slower than V2's blazing-fast single-threaded loop. The V3 logic is intentionally heavy because it mimics the 3-stage barrier synchronization required for a GPU.
2. **Algorithmic Success**: Despite the CPU threading overhead, the mathematical output is 100% correct. We successfully decoupled the carry dependency across chunks.
3. **Prepared for GPU**: This exact "Scan & Apply" architecture maps perfectly to CUDA's Grid/Block architecture, where each chunk becomes a Block, and the `carryBuffer` prefix sum can be resolved efficiently.

## Next Steps
Now that we have a parallel-friendly reduction algorithm and a working GPU connection, we combine them. We will use GPU `cuFFT` for squaring and the V3 CPU parallel logic for reduction. This creates the first Full Hybrid Pipeline (Phase 4).
