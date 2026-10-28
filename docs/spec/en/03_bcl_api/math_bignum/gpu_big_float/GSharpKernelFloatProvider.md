---
title: "GSharpKernelFloatProvider Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Execution provider implementing arbitrary-precision floating-point arithmetic via native CUDA kernels, GSharp AOT kernels, and CPU fallback strategies."
---

## GSharpKernelFloatProvider Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Execution provider implementing [`IGpuBigFloatArithmetic`](IGpuBigFloatArithmetic.md) by executing high-precision floating-point operations natively on the GPU via compiled G# device kernels (`GpuBigFloatKernels`, `GpuBigIntMath`, `GpuBigIntDivRem`), with CPU fallback orchestration ([`CSharpCpuBigFloatArithmetic`](CSharpCpuBigFloatArithmetic.md)).

```csharp
public sealed class GSharpKernelFloatProvider(IGpuBigFloatArithmetic fallbackStrategy)
    : IGpuBigFloatArithmetic
```

## Properties

| Name | Description |
| --- | --- |
| `Instance` | Gets the default singleton instance of `GSharpKernelFloatProvider` using `CSharpCpuBigFloatArithmetic.Instance` as fallback. |

## Methods

| Name | Description |
| --- | --- |
| [CreateGSharpKernel()](#creategsharpkernel) | Gets the singleton instance of `GSharpKernelFloatProvider`. |
| `Normalize(...)` | Normalizes a floating-point significand so that its most significant bit is aligned to the target word width. |
| `Add(...)` | Computes radix-aligned signed addition of two arbitrary-precision floating-point numbers. |
| `Multiply(...)` | Computes normalized multiplication of two arbitrary-precision floating-point numbers. |
| `Divide(...)` | Computes $\mathrm{RZ}_p(x / y)$: the exact quotient of the dividend scaled to at least $p + 1$ quotient bits, truncated to $p$ bits; a divisor much wider than the result is truncated and the quotient corrected (see [Wide Divisors and Radicands](#wide-divisors-and-radicands)). |
| `Reciprocal(...)` | Computes the normalized reciprocal ($1 / x$) via G# device kernels. |
| `Sqrt(...)` | Computes $\mathrm{RZ}_p(\sqrt{x})$: the exact integer square root of the value scaled to $2p$ bits, from an inverse square root estimate and a correction on the device. |
| `Rsqrt(...)` | Computes $\mathrm{RZ}_p(1 / \sqrt{x})$: the exact $\lfloor 2^B / \sqrt{A} \rfloor$ of the value scaled to whole words without truncation, with enough margin words for $p + 1$ bits, truncated to $p$ bits; a radicand much wider than the result is truncated and the root corrected (see [Wide Divisors and Radicands](#wide-divisors-and-radicands)). |
| `Exp(...)` | Computes $\mathrm{RZ}_p(e^x)$ on the GPU: reduction by $k \ln 2$ and $2^s$, a Paterson–Stockmeyer Taylor polynomial, $s$ squarings and the rounding check (see [Transcendental Functions](#transcendental-functions)). |
| `Log(...)` | Computes $\mathrm{RZ}_p(\ln x)$ on the GPU: reduction by $2^e$ and an $\mathrm{atanh}$ series in Horner or Paterson–Stockmeyer form, with the rounding check. |
| `Pow(...)` | Computes $\mathrm{RZ}_p(x^y)$ for $x > 0$: an exact power directly, every other power as $e^{y \cdot L}$ on the GPU with the rounding check. |
| `DivideByUInt32(...)` | Divides the significand by a 32-bit unsigned integer scalar and outputs the remainder. |

## CreateGSharpKernel()

Creates an AOT-compiled provider that uses G# kernels for high-precision floating-point operations.

```csharp
public static GSharpKernelFloatProvider CreateGSharpKernel();
```

#### Returns

_GSharpKernelFloatProvider_  
A new `GSharpKernelFloatProvider` instance configured for G# kernel operations.

## Examples

### Creating a GSharpKernelFloatProvider Instance

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;

IGpuBigFloatArithmetic provider = GSharpKernelFloatProvider.CreateGSharpKernel();
Console.WriteLine($"Created float provider: {provider.GetType().Name}");
```

## Remarks

`GSharpKernelFloatProvider` manages temporary device scratch buffers deterministically using `context.GpuWorkspace.RentTemp(...)` without managed heap allocations on the GPU hot path. A rented buffer is owned by the caller and goes back to the workspace pool when it is disposed (`using`).

In CPU fallback mode (no `GSharpKernelProvider`, or `GpuKernelLoader.IsCpuFallbackMode`) every operation, `Pow` included, is forwarded once to the fallback strategy.

### Wide Divisors and Radicands

For a result of $n$ words ($p = 32n$ bits), `Divide` and `Rsqrt` cut a divisor or radicand of at least $n + 2 + g$ words to its top $K = n + 2$ words, where $g$ is [`GpuWorkspace.WideOperandThresholdWords`](../gpu_workspace/GpuWorkspace.md#wideoperandthresholdwords) (default 4). The result is the same bits as without the cut; only the work changes.

- The integer computed from the top words, the quotient $Q_{hi}$ or the inverse root $x_{hi}$, is the exact floor or one above it. A smaller divisor or radicand makes the result larger, never smaller, and the 64 extra bits of the top words keep the excess below 1.
- When a bit that the rounding to $p$ bits discards is set (one of the low 2 bits of $Q_{hi}$, which has $p + 2$ or $p + 3$ bits; the low word of $x_{hi}$, which has at least $p + 33$ bits), the floor and the value one above it round to the same $p$ bits, and the result is used as it is. Only the low word is read back.
- Otherwise one product at the full width decides: $Q_{hi} \cdot R > N$ (the scaled dividend), or $x_{hi}^2 \cdot A > 2^{2B}$, means the estimate is one too large. A one-block kernel compares and decrements it on the device, without a read back. The divisor cut by nothing ($R$ of $K$ words or fewer bits) needs no check.

This is the form of MPFR's `mpfr_div` for a divisor longer than the quotient (`src/div.c`), with the check made by one multiplication when the discarded bits leave the result open. The CPU fallback does not truncate.

The cut pays off when the full-width computation is in a slower size range of the GPU division and inverse square root than the cut one. Measured on one GPU: for $n \le 16$ a divisor of about 30 words or more and a radicand of about 64 words or more run 5 to 15 times faster; below those sizes both forms take about the same time (ratios of 0.9 to 1.1 are within the variation of the fixed costs). For $n \ge 32$ the top $K$ words are already in the slow range, and the cut form was up to 2 times slower for operands of $4n$ to $16n$ words. That ratio comes mostly from the full-width computation getting faster with the width in that range, not from the cut form getting slower: at $n = 64$ the cut `Rsqrt` takes about 24 ms at every width, while the full-width one drops from 24 ms (66 words) to 14 to 17 ms (264 to 1024 words); the full-width `Divide` drops from 15 ms (68 words) to 11 ms (1024 words), while the cut one takes 16 to 18 ms up to 264 words and 24 ms at 1024 words. Set $g$ to `int.MaxValue` to turn the cut off.

### Transcendental Functions

`Exp`, `Log` and `Pow` return the exact value rounded toward zero to the $p = 32n$ bits of the result words, for every argument in range, and the same bits as [`CSharpCpuBigFloatArithmetic`](CSharpCpuBigFloatArithmetic.md): both run the same host-side plan (term counts, block sizes, working precision $W = p + 32 \cdot \text{guard words}$) and the same integer truncations. The plan is made before any device work, so the series run without reading values back; the host waits for the device to read the leading bits of the argument, to normalize the approximation and to read its guard words for the rounding check. An approximation is accepted only when every value within its error bound truncates to the same $p$ bits; otherwise it is recomputed with more guard words ($W$ grows by half, at least one word).

| Function | Algorithm |
| --- | --- |
| `Exp` | $x = k \ln 2 + r$, $r \in (0.69, 1.39)$ for either sign (ln 2 from a cached Machin-type constant); the Taylor polynomial of $e^{r/2^s}$ in Paterson–Stockmeyer form with Smith's division order; $s$ squarings; $e^x = e^r \cdot 2^k$. $\lvert x \rvert < 2^{-(p+1)}$ returns 1 or $1 - 2^{-p}$ directly; $\lvert x \rvert \ge 2^{32}$ throws `OverflowException`. |
| `Log` | $x = m \cdot 2^e$, $m \in [3/4, 3/2)$; $\ln x = e \ln 2 + 2y \cdot T(y^2)$, $y = (m - 1)/(m + 1)$ kept with its own exponent (an argument close to 1 loses no bits); $T$ in Horner form up to `AtanhHornerMaxTerms` terms, Paterson–Stockmeyer otherwise. |
| `Pow` | The exact check (as MPFR's `mpfr_pow_is_exact`) on the host returns a $p$-bit power such as $2^3$ or $4^{0.5}$ directly. Otherwise $L \approx \ln x$ at the Exp precision plus 64 bits, $y \cdot L$ exactly on the device, its exponential, and the rounding check with the Exp bound plus the error of $L$. |

The parameters $s$, $m$ and the Horner limit can be fixed per workspace through [`GpuBigFloatTranscendentalOptions`](GpuBigFloatTranscendentalOptions.md) (`GpuWorkspace.Transcendental`); they change only the speed.

All precisions use the algorithms above. Faster algorithms for large precisions (Newton's iteration for `Log`, bit-burst binary splitting for `Exp`) and a one-kernel evaluation of short vectors are not implemented.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Floats (GpuBigFloat)](00_index.md)
- [IGpuBigFloatArithmetic](IGpuBigFloatArithmetic.md)
- [CSharpCpuBigFloatArithmetic](CSharpCpuBigFloatArithmetic.md)
- [GpuBigFloatTranscendentalOptions](GpuBigFloatTranscendentalOptions.md)
