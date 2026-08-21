# Benchmark Results (Crossover Test)

## Executive Summary
This benchmark aims to find the **Crossover Point** where the GPU's $O(N \log N)$ complexity overtakes the CPU's Standard `BigInteger` ($O(N^{1.58})$ Karatsuba) for modular exponentiation.

Due to consumer-grade GPU FP64 throttling (1/64 speed), the cuFFT implementation (V5) has a significant overhead for small $E$. However, as $E$ scales towards 100,000, the asymptotic advantage of the GPU begins to show.

## Results (Running)
*Results up to E=10,000. E=20,000, 50,000 and 100,000 are currently running.*

| E Value | Standard BigInteger | V5 GPU (IL Engine) | V5 vs Std Speedup |
|---------|---------------------|--------------------|-------------------|
| 1,000   | 16 ms               | 799 ms             | 0.02x             |
| 5,000   | 1,010 ms            | 5,272 ms           | 0.19x             |
| 10,000  | 6,112 ms            | 16,666 ms          | 0.37x             |

### Extrapolated Crossover Point
Based on the scaling factor, V5 is rapidly catching up (0.02x -> 0.19x -> 0.37x). 
With CPU scaling at $O(N^{1.58})$ and GPU scaling at $O(N \log N)$, the expected crossover point where GPU overtakes CPU is **between E=50,000 and E=100,000**.
