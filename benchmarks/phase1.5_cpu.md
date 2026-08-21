# Phase 1.5: Custom Reduction vs StdLib (CPU)

## Objective
To prove that a custom $O(N)$ modular reduction algorithm tailored specifically to Fermat numbers ($N = 2^{2E+1} - 2^E + 11$) can outperform the highly optimized generic `BigInteger.ModPow` provided by the .NET Standard Library.

## Architecture
- **Execution Model**: Single-threaded CPU execution (C# .NET 10.0).
- **V1 (Naive Custom)**: A direct translation of the custom mathematical formula ($R + (Q \ll E) - 11 \cdot Q$) into array manipulation.
- **V2 (Ultra Optimized)**: Absolute limits of CPU optimization applied to V1. Eliminated branch prediction misses, removed dynamic memory allocations, statically unrolled the Fermat N-1 bitwise exponentiation loop, and used hardware intrinsics where applicable.

## Benchmark Results

| E | Digits (Approx) | Standard BigInteger | Custom V1 | Ultra Optimized V2 |
|---|---|---|---|---|
| 10 | 30 | 0 ms | 0 ms | 0 ms |
| 500 | 1,500 | 3 ms | 4 ms | 2 ms |
| 1000 | 3,000 | 18 ms | 20 ms | 7 ms |
| 5000 | 15,000 | 1091 ms | 716 ms | 227 ms |
| 10000 | 30,000 | 6004 ms | 4135 ms | 1276 ms |

*(Note: Data measured on Ryzen 9 7900X, Single Thread)*

## Insights & Analysis
1. **Beating the Standard Library**: By $E=10000$, our V2 implementation runs roughly **4.7x faster** than the standard `BigInteger.ModPow`. This proves that our custom algorithm mathematically requires far fewer CPU cycles than standard generic division.
2. **The $O(N^2)$ Trap**: Despite the massive speedup, V2 still relies on $O(N^2)$ multiplication (or similar sub-quadratic methods on the CPU). Notice how the execution time balloons from 227ms to 1276ms as $E$ doubles from 5000 to 10000. It scales exponentially, meaning $E=43M$ would still take hundreds of thousands of years on a CPU.

## Next Steps
To beat the $O(N^2)$ scaling wall of CPU multiplication, we must transition to $O(N \log N)$ FFT-based multiplication. This pushes us toward utilizing GPUs (cuFFT), leading to Phase 2.
