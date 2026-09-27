# GPU Benchmark Results & Standard Formats

This document defines the standardized benchmark reporting format for Henicos (G#).
When publishing, discussing, or comparing benchmark results in GitHub Issues, Pull Requests, or documentation, adhere strictly to the **mandatory Environment Header Block**, **Host/Device Split Memory Allocation Column**, and **Hardware Resource Metrics (Registers / Instruction Counts)** defined below.

---

## Standard Format Specifications

### 1. Mandatory Requirement: Hardware / Environment Metadata Block
GPU benchmark metrics lose substantial diagnostic and comparative value without precise context regarding the underlying hardware's theoretical peak performance (TFLOPS) and memory bandwidth (GB/s).
Therefore, **every benchmark table must be preceded by an Environment metadata block**.

```markdown
### Environment
- **OS**: Windows 11 Pro (NT 10.0.26200)
- **CPU**: AMD Ryzen 9 9900X 12-Core Processor (24 Threads)
- **RAM**: 64 GB DDR5
- **GPU**: NVIDIA GeForce RTX 3070 Ti (48 SMs / 6,144 CUDA Cores / 8 GB GDDR6X)
- **Compute Capability**: sm_86 (Ampere)
- **Driver / CUDA**: Driver 610.88 / CUDA Toolkit 13.4 / UMD 13.3
- **Theoretical Peak**: 21.75 TFLOPS (FP32) / 608 GB/s (Memory Bandwidth)
- **Runtime**: .NET 10.0 (RyuJIT X64) / InProcessEmitToolchain
```

---

### 2. Mandatory Requirement: Host / Device Split Memory Notation
In GPU computing, **host-side managed heap allocations (subject to GC overhead and pauses)** and **device-side VRAM consumption** represent fundamentally distinct operational characteristics.
To explicitly substantiate G#'s core design tenet of **Zero-Allocation (zero runtime GC pressure)**, the allocated memory column must explicitly delineate memory domains using the `Host: <size> / Dev: <size>` notation.

| Format Syntax | Meaning | Typical Scenario |
| :--- | :--- | :--- |
| `Host: 0 B / Dev: 128 MB` | **Zero-Allocation Guaranteed** (GC-free execution; VRAM only) | Optimized hot path and batch kernel execution |
| `Host: 288 B / Dev: -` | Minor host-side wrapper allocation detected | Single-invocation parameter arrays or delegate captures |
| `Host: 0 B / Dev: 0 B` | Zero dynamic allocations (pre-allocated pool reuse) | Steady-state execution loops after warmup |
| `-` | Memory metric unmeasured | Simple duration timer measurements |

---

### 3. Hardware Resource & Instruction Count Metrics
To verify kernel compilation efficiency and hardware occupancy, benchmarks should report device-level resource usage queried via `cuFuncGetAttribute` and binary inspection:

| Metric | Query Mechanism | Purpose & Architectural Significance |
| :--- | :--- | :--- |
| **Regs / Thread** | `cuFuncGetAttribute(CU_FUNC_ATTRIBUTE_NUM_REGS)` | Thread register allocation. Directly impacts SM warp occupancy. |
| **Local Spill** | `cuFuncGetAttribute(CU_FUNC_ATTRIBUTE_LOCAL_SIZE_BYTES)` | Local memory spill size. Identifies register pressure causing VRAM spill. |
| **SASS / PTX Insts** | `cuobjdump -sass` or `-ptx` | Actual compiled machine instructions. Proves transpiler code generation optimality. |

---

### 4. Compact Summary Tables (Markdown Standard)

#### Benchmark Execution Time & Speedup Summary
| Benchmark | CPU (ms) | Native (ms) | G# (ms) | Ratio | Speedup vs CPU |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **SGEMM (512x512)** | 112.17 | 1.41 ms | 1.42 ms | **1.00x** | **79.1x** |
| **SHA-256 (100k Blocks)** | 132.59 | 1.92 ms | 1.83 ms | **0.95x (Faster)** | **72.4x** |
| **Black-Scholes (Float)** | 22.20 | 3.37 ms | 3.57 ms | **1.06x** | **6.2x** |
| **Parallel Reduction** | 4.21 | 9.87 ms | 8.98 ms | **0.91x (Faster)** | **0.5x** |

#### Kernel Hardware Resources & Instruction Summary
| Benchmark | Regs (Nat / G#) | Local Spill (Nat / G#) | SASS Insts (Nat / G#) |
| :--- | :--- | :--- | :--- |
| **SGEMM (512x512)** | **38 / 38** | **0 / 0 B** | **103 / 97** |
| **SHA-256 (100k Blocks)** | **56 / 59** | **256 / 512 B** | **742 / 821** |
| **Black-Scholes (Float)** | **21 / 22** | **0 / 0 B** | **286 / 293** |
| **Parallel Reduction** | **18 / 18** | **0 / 0 B** | **43 / 52** |

---

### 5. Common Table Column Definitions

| Column Name | Description | Example Values |
| :--- | :--- | :--- |
| **Operation** | Kernel, algorithm, or operation name | `**SGEMM (G# CUDA)**`, `**Multiply**` |
| **Workload / Dimensions** | Problem scale (Bits/Words, Matrix Size $M \times N \times K$, Element Count) | `2048 words (65,536 bits)`, `512 × 512 × 512` |
| **Batch Size** | Number of concurrent batch items (single launch is `1`) | `1`, `64`, `1024` |
| **Regs / Thread** | Hardware registers allocated per CUDA thread | `38 regs`, `22 regs` |
| **Local Spill** | Local memory spill bytes | `0 B` (Optimal), `256 B` |
| **SASS Insts** | Compiled assembly instruction count | `97 insts`, `293 insts` |
| **Mean Time (Total)** | Total elapsed execution time including host/device overhead | `1.31 ms`, `473.7 μs`, `DNF (Timeout)` |
| **Throughput** | Throughput per second (FLOPS, Hash Rate, or Operations/sec) | `218.2 GFLOPS`, `52.08 MH/s` |
| **Relative to Baseline** | Performance multiplier relative to the baseline implementation | `**1.00x** (Baseline)`, `**1.01x**`, `88.1x (slower)` |
| **Allocated Memory** | **Host (GC) and Device (VRAM) split allocation** | `Host: 0 B / Dev: 3.0 MB` |

---

## 1. GPU Big Math Benchmark Results (65,536 bits)

### Environment
- **OS**: Windows 11 Pro (NT 10.0.26200)
- **CPU**: AMD Ryzen 9 9900X 12-Core Processor (24 Threads)
- **GPU**: NVIDIA GeForce RTX 3070 Ti (48 SMs / 8 GB GDDR6X)
- **Driver / CUDA**: Driver 610.88 / CUDA Toolkit 13.4
- **Theoretical Peak**: 21.75 TFLOPS (FP32) / 608 GB/s (Memory Bandwidth)
- **Runtime**: .NET 10.0 (RyuJIT X64) / BenchmarkDotNet v0.14.0

| Operation | Words | Bits | Batch Size | Mean Time (Total) | Kernel Time (Device) | Ops/sec (Throughput) | Relative to Mul | Allocated Memory (Host / Dev) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Multiply** | 2048 | 65,536 | 1 | 473.7 μs | - | 2,111 Ops/s | **1.00x** (Baseline) | **Host: 288 B / Dev: -** |
| **Divide** | 2048 | 65,536 | 1 | 36.41 ms | - | 27 Ops/s | **77.43x** | **Host: 21.41 KB / Dev: -** |
| **Sqrt** | 2048 | 65,536 | 1 | 167.72 ms | - | 6 Ops/s | **356.65x** | **Host: 140.76 KB / Dev: -** |
| **Multiply** | 2048 | 65,536 | 1024 | 572.59 ms | - | 1,788 Ops/s | **1.00x** (Baseline) | **Host: 0 B / Dev: -** |
| **Divide** | 2048 | 65,536 | 1024 | DNF (Timeout) | - | - | - | - |
| **Sqrt** | 2048 | 65,536 | 1024 | DNF (Timeout) | - | - | - | - |

---

## 2. SGEMM (Dense Matrix Multiplication) Benchmark Results

### Environment
- **OS**: Windows 11 Pro (NT 10.0.26200)
- **CPU**: AMD Ryzen 9 9900X 12-Core Processor (24 Threads)
- **GPU**: NVIDIA GeForce RTX 3070 Ti (48 SMs / 8 GB GDDR6X)
- **Driver / CUDA**: Driver 610.88 / CUDA Toolkit 13.4
- **Theoretical Peak**: 21.75 TFLOPS (FP32) / 608 GB/s (Memory Bandwidth)
- **Runtime**: .NET 10.0 (RyuJIT X64)

| Operation | Matrix Size ($M \times N \times K$) | Regs / Thread | Local Spill | SASS Insts | Mean Time | Throughput (TFLOPS) | Relative to Baseline | Allocated Memory (Host / Dev) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **SGEMM (Native CUDA)** | 512 × 512 × 512 | **38 regs** | **0 B** | **103** | 1.30 ms | 0.206 TFLOPS | **1.00x** (Baseline) | **Host: 0 B / Dev: 3.0 MB** |
| **SGEMM (G# CUDA)** | 512 × 512 × 512 | **38 regs** | **0 B** | **97** | 1.31 ms | 0.205 TFLOPS | **1.01x** | **Host: 0 B / Dev: 3.0 MB** |
| **SGEMM (CPU Ref)** | 512 × 512 × 512 | N/A | N/A | N/A | 107.10 ms | 0.002 TFLOPS | **81.6x (slower)** | **Host: 3.0 MB / Dev: -** |

> **Throughput Formula**:
> $$\text{TFLOPS} = \frac{2 \times M \times N \times K \times \text{BatchSize}}{\text{Mean Time (Seconds)} \times 10^{12}}$$

---

## 3. General Compute Benchmarks (Black-Scholes, Reduction, SHA-256)

### Environment
- **OS**: Windows 11 Pro (NT 10.0.26200)
- **CPU**: AMD Ryzen 9 9900X 12-Core Processor (24 Threads)
- **GPU**: NVIDIA GeForce RTX 3070 Ti (48 SMs / 8 GB GDDR6X)
- **Driver / CUDA**: Driver 610.88 / CUDA Toolkit 13.4
- **Theoretical Peak**: 21.75 TFLOPS (FP32) / 608 GB/s (Memory Bandwidth)
- **Runtime**: .NET 10.0 (RyuJIT X64)

### Cryptographic Hash (SHA-256 Compression)
- **Workload**: 100,000 message blocks (64 bytes each, 6.4 MB payload)

| Operation | Block Size / Messages | Regs / Thread | Local Spill | SASS Insts | Mean Time | Hash Rate (MH/s) | Relative to Baseline | Allocated Memory (Host / Dev) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **SHA-256 (Native CUDA)** | 64 B / 100,000 | **56 regs** | **256 B** | **742** | 1.88 ms | 53.19 MH/s | **1.00x** (Baseline) | **Host: 0 B / Dev: 9.6 MB** |
| **SHA-256 (G# CUDA)** | 64 B / 100,000 | **59 regs** | **512 B** | **821** | 1.83 ms | 54.64 MH/s | **0.97x (Faster)** | **Host: 0 B / Dev: 9.6 MB** |
| **SHA-256 (CPU Ref)** | 64 B / 100,000 | N/A | N/A | N/A | 128.12 ms | 0.78 MH/s | **70.0x (slower)** | **Host: 9.6 MB / Dev: -** |

---

### Black-Scholes (Option Pricing)
- **Workload**: 1,000,000 European call/put options computed in single-precision floating point

| Operation | Options Count | Regs / Thread | Local Spill | SASS Insts | Mean Time | Throughput (MOps/s) | Relative to Baseline | Allocated Memory (Host / Dev) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Black-Scholes (Native CUDA)** | 1,000,000 | **21 regs** | **0 B** | **286** | 3.21 ms | 311.5 MOps/s | **1.00x** (Baseline) | **Host: 0 B / Dev: 20.0 MB** |
| **Black-Scholes (G# CUDA)** | 1,000,000 | **22 regs** | **0 B** | **293** | 3.24 ms | 308.6 MOps/s | **1.01x** | **Host: 0 B / Dev: 20.0 MB** |
| **Black-Scholes (CPU Ref)** | 1,000,000 | N/A | N/A | N/A | 22.08 ms | 45.3 MOps/s | **6.8x (slower)** | **Host: 20.0 MB / Dev: -** |

---

### Parallel Reduction (Sum)
- **Workload**: 16,777,216 floating-point elements (67.1 MB), reduced via warp shuffles

| Operation | Elements Count | Regs / Thread | Local Spill | SASS Insts | Mean Time | Effective Bandwidth | Relative to Baseline | Allocated Memory (Host / Dev) |
| :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- | :--- |
| **Reduction (Native CUDA)** | 16,777,216 | **18 regs** | **0 B** | **43** | 7.65 ms | 8.77 GB/s | **1.00x** (Baseline) | **Host: 0 B / Dev: 67.2 MB** |
| **Reduction (G# CUDA)** | 16,777,216 | **18 regs** | **0 B** | **52** | 7.36 ms | 9.12 GB/s | **0.96x (Faster)** | **Host: 0 B / Dev: 67.2 MB** |
| **Reduction (CPU Ref)** | 16,777,216 | N/A | N/A | N/A | 4.53 ms | 14.81 GB/s | **0.62x** (Cached CPU) | **Host: 67.1 MB / Dev: -** |
