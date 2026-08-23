# Phase 6: VRAM Reduction Parallelization

## Objective
To utilize the massively parallel architecture of the GPU by porting the "Scan & Apply" reduction chunking logic from Phase 3 into CUDA kernels, breaking the single-thread bottleneck of Phase 5. Simultaneously, fix the mathematical logic mismatch to achieve a 100% correct Fermat PRP test (Pass: True).

## Architecture
- **Execution Model**: Full Fermat loop unrolled and executed in VRAM.
- **Reduction Logic**:
  - `ReduceChunkKernel`: Divides the large array into fixed-size chunks (e.g., 256 words per chunk). Each thread handles one chunk sequentially, extracting the local carry.
  - `ApplyCarryKernel`: A synchronization step that performs a Prefix Sum on the generated carries, then applies the true Ripple Carry to each chunk.
- **Correction**: The loop execution logic inside `RunFermatLoopV5` was updated to accurately mirror the $O(1)$ static unrolling bit-sequence proven in Phase 2.

## Benchmark Results
Metric: Mega-Digits processed per second (M-Digit/s)
*(Data recorded after fixing the logical mismatch to `Pass: True`)*

| E | V2 (Ultra Opt CPU) | V4 (Hybrid GPU) | V5 (Zero-Overhead VRAM) |
|---|---|---|---|
| 1000 | 66.93 M-Digit/s | 4.27 M-Digit/s | 1.44 M-Digit/s |
| 2000 | 80.29 M-Digit/s | 6.32 M-Digit/s | 1.69 M-Digit/s |
| 5000 | 68.42 M-Digit/s | 7.92 M-Digit/s | 2.47 M-Digit/s |
| 10000 | 47.90 M-Digit/s | 8.46 M-Digit/s | 3.22 M-Digit/s |

## Insights & Analysis
1. **Mathematical Accuracy Achieved**: By accurately mapping the `Square` and `Mul2` sequence inside the CUDA host loop, V5 successfully output `Pass: True` for all $E$ values up to 10000.
2. **The "GPU Under-utilization" Trap**: Despite parallelizing the reduction kernel, V5 is now severely slower than the single-threaded CPU version (V2). Why?
   - **Lack of Scale**: At $E=10000$, the array is only 625 words long. With a chunk size of 256, this spawns a mere **3 CUDA threads**. We are trying to beat a 5GHz CPU core using only 3 slow 1GHz GPU cores. The GPU's strength lies in launching tens of thousands of threads, which doesn't happen until much larger values of $E$.
   - **Single-Threaded Normalization**: The `NormalizeKernel` is still running sequentially (`<<<1, 1>>>`), acting as a severe bottleneck. 

## Next Steps
Phase 6 proved that mathematical correctness can be achieved entirely inside VRAM. To fix the GPU under-utilization at smaller scales, we must dynamically adjust the `chunkSize` based on the array length to ensure we always spawn thousands of threads. Additionally, we must parallelize or completely eliminate the heavy `NormalizeKernel`.
