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
| `Add(...)` | Computes $\mathrm{RZ}_p(x + y)$ from the exact sum of the significands aligned at the smaller exponent; an operand far below the other is reduced to a sticky bit (see [Far-Apart Exponents](#far-apart-exponents)). |
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

### Far-Apart Exponents

For a result of $n$ words ($p = 32n$ bits), let $H$ be the operand of `Add` with the larger exponent (on a tie, the wider significand) and $L$ the other one, with $w_L$ words. `Add` shifts $H$ left onto $L$ and adds exactly, so the work would grow with $e_H - e_L$. When $e_L + 32 w_L \le e_H - p$, $L$ lies below $2^{e_H - p}$, and every sum $H \pm x$ with $0 < x < 2^{e_H - p}$ rounds to the same $p$ bits: a nonzero $L$ is replaced by the sticky bit $2^{e_H - p - 1}$, and a zero $L$ or $H$ leaves the other operand. The work then depends on the widths only. The exponents are subtracted in `long`, so a difference beyond `int` (up to $2^{32} - 1$) does not wrap around. This is the form of MPFR's `mpfr_add1sp` / `mpfr_sub1sp` for an exponent difference of at least $p$. [`CSharpCpuBigFloatArithmetic`](CSharpCpuBigFloatArithmetic.md) uses the same rule.

### Wide Divisors and Radicands

For a result of $n$ words ($p = 32n$ bits), `Divide` and `Rsqrt` cut a divisor or radicand of at least $n + 2 + g$ words ($g = 4$) to its top $K = n + 2$ words, within the widths where the cut is faster (below). The result is the same bits as without the cut; only the work changes.

- The integer computed from the top words, the quotient $Q_{hi}$ or the inverse root $x_{hi}$, is the exact floor or one above it. A smaller divisor or radicand makes the result larger, never smaller, and the 64 extra bits of the top words keep the excess below 1.
- When a bit that the rounding to $p$ bits discards is set (one of the low 2 bits of $Q_{hi}$, which has $p + 2$ or $p + 3$ bits; the low word of $x_{hi}$, which has at least $p + 33$ bits), the floor and the value one above it round to the same $p$ bits, and the result is used as it is. Only the low word is read back.
- Otherwise one product at the full width decides: $Q_{hi} \cdot R > N$ (the scaled dividend), or $x_{hi}^2 \cdot A > 2^{2B}$, means the estimate is one too large. A one-block kernel compares and decrements it on the device, without a read back. The divisor cut by nothing ($R$ of $K$ words or fewer bits) needs no check.

This is the form of MPFR's `mpfr_div` for a divisor longer than the quotient (`src/div.c`), with the check made by one multiplication when the discarded bits leave the result open. The CPU fallback does not truncate.

The cut pays off only when it moves the work out of the slow size range of the GPU division or inverse square root. The division takes about 2 ms for a divisor of up to 32 words and 11 to 17 ms for a wider one; the inverse square root of the cut radicand takes at most 1.7 ms up to $n = 44$ and 24 ms from $n = 45$. So the cut is used only within these widths:

| Operation | Cut when | Reason |
| --- | --- | --- |
| `Divide` | $n \le 30$ and a divisor of at least 33 words | The cut divisor of $n + 2$ words stays in the fast range; a divisor of up to 32 words is in it already |
| `Rsqrt` | $n \le 44$ | The inverse square root of the top words stays in its fast range |

Measured with `Henicos.Benchmarks arithmetic wide` on an RTX 3070 Ti (time with the cut / time without, operands of $w$ words):

| $n$ | $w$ | `Divide` | `Rsqrt` |
| --: | --: | --: | --: |
| 8 | 14 | 1.05 | 1.06 |
| 8 | 32 | 0.88 | 0.66 |
| 8 | 128 | 0.12 | 0.05 |
| 16 | 34 | 0.16 | 0.86 |
| 30 | 120 | 0.15 | 0.06 |
| 30 | 480 | 0.16 | 0.09 |
| 31 | 124 | 0.96 | 0.06 |
| 32 | 512 | 1.23 | 0.09 |
| 44 | 176 | 1.44 | 0.08 |
| 45 | 180 | 1.42 | 1.06 |
| 64 | 1024 | 1.51 | 1.68 |

For a divisor of 32 words or fewer, the `Divide` ratios varied between 0.87 and 1.19 over four runs, with no consistent gain, so the full width is kept there. Above those widths the cut form is slower mostly because the full-width computation gets faster with the width there (the full-width `Divide` takes 15 ms at 124 words and 11 ms at 1024 words), while the cut one stays at about 16 ms for `Divide` and 24 ms for `Rsqrt`. The limits and $g$ are internal.

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
