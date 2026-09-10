# 12. Arbitrary-Precision GPU Floating-Point Module (GpuBigFloat / GpuBigUFloat)

The `Kassyi.GSharp.Stdlib.Math.BigFloat` namespace provides high-performance, arbitrary-precision floating-point arithmetic executed natively on NVIDIA GPUs. Designed according to G#'s core tenets of **Zero-Allocation** and **Static Determinism**, all floating-point types are represented as unmanaged `struct` value types.

---

## 12.1 Overview & Core Architecture

Arbitrary-precision GPU floating-point operations require predictable memory access, zero dynamic heap allocations, and zero dynamic dispatch. The module defines two primary floating-point value types:

1. **`GpuBigUFloat<TSize>`**: Unsigned arbitrary-precision floating-point number.
2. **`GpuBigFloat<TSize>`**: Signed arbitrary-precision floating-point number wrapping `GpuBigUFloat<TSize>`.

### 12.1.1 Structural Layout & Memory Safety

To guarantee static determinism and seamless mapping to CUDA C++ registers and shared memory, all types enforce sequential layout (`[StructLayout(LayoutKind.Sequential)]`):

```csharp
namespace Kassyi.GSharp.Stdlib.Math.BigInt;

[StructLayout(LayoutKind.Sequential)]
public struct GpuUIntBigInteger<TSize> where TSize : unmanaged
{
    public TSize Limbs;
}

namespace Kassyi.GSharp.Stdlib.Math.BigFloat;

[StructLayout(LayoutKind.Sequential)]
public struct GpuBigUFloat<TSize> where TSize : unmanaged
{
    public GpuUIntBigInteger<TSize> Significand;
    public int Exponent;
}

[StructLayout(LayoutKind.Sequential)]
public struct GpuBigFloat<TSize> where TSize : unmanaged
{
    public GpuBigUFloat<TSize> UFloat;
    public bool IsNegative;
}
```

- **`Significand` (Mantissa)**: Fixed-size unsigned big integer representing the significant digits.
- **`Exponent`**: 32-bit signed integer representing the binary power ($2^{\text{Exponent}}$).
- **`IsNegative`**: Boolean flag indicating numerical sign (`true` for negative, `false` for non-negative).
- **Zero-Allocation Guarantee**: Instances are stack- or register-allocated value types. No reference allocations, interfaces, or virtual calls are permitted on GPU device code.

---

## 12.2 Radix Point Alignment (Bit-Shifting)

When performing addition ($A + B$) or subtraction ($A - B$), the operands must be aligned to a common radix point before significand operations.

Given two operands:
$$A = (-1)^{S_A} \cdot M_A \cdot 2^{E_A}$$
$$B = (-1)^{S_B} \cdot M_B \cdot 2^{E_B}$$

### 12.2.1 Alignment Algorithm

1. **Exponent Comparison**:
   Calculate the exponent difference $\Delta E = |E_A - E_B|$.
2. **Target Exponent Selection**:
   Set the common target exponent $E_{\text{common}} = \max(E_A, E_B)$.
3. **Significand Bit-Shifting**:
   - The operand with the smaller exponent must have its significand shifted right by $\Delta E$ bits.
   - If $E_A < E_B$: $M_A' = M_A \gg \Delta E$.
   - If $E_B < E_A$: $M_B' = M_B \gg \Delta E$.
4. **Shift Overflow & Sticky Bit Preservation**:
   - If $\Delta E \ge \text{BitLength}(M)$, the smaller operand's significand shifts completely past the precision window.
   - Shifted-out bits are collapsed into a **Sticky Bit** ($S$) to preserve rounding accuracy during IEEE 754 ties-to-even evaluation.

---

## 12.3 Normalization & IEEE 754 Compliant Rounding

### 12.3.1 Normalization Process

Following an arithmetic operation, the resulting significand $M_{\text{raw}}$ may overflow (carry out) or underflow (leading zeros). Normalization adjusts $M_{\text{raw}}$ so that its Most Significant Bit (MSB) aligns with the top bit of the allocated significand size:

1. **Overflow Normalization**:
   If addition produces a carry bit, shift $M_{\text{raw}}$ right by 1 bit and increment $E$:
   $$M_{\text{norm}} = M_{\text{raw}} \gg 1, \quad E_{\text{norm}} = E_{\text{common}} + 1$$
2. **Underflow Normalization**:
   If subtraction results in leading zeros, compute Leading Zero Count $\text{LZC}(M_{\text{raw}})$, shift $M_{\text{raw}}$ left by $\text{LZC}$, and decrement $E$:
   $$M_{\text{norm}} = M_{\text{raw}} \ll \text{LZC}, \quad E_{\text{norm}} = E_{\text{common}} - \text{LZC}$$

### 12.3.2 IEEE 754 Rounding Modes (`GpuBigFloatRoundingMode`)

The module supports IEEE 754 compliant rounding modes defined in `GpuBigFloatRoundingMode`:

```csharp
public enum GpuBigFloatRoundingMode
{
    RoundToNearestEven = 0,
    RoundTowardZero = 1,
    RoundTowardPositiveInfinity = 2,
    RoundTowardNegativeInfinity = 3
}
```

#### Round to Nearest, Ties to Even (`RoundToNearestEven`)
Evaluated using Guard Bit ($G$), Round Bit ($R$), and Sticky Bit ($S$):
- **$G$ (Guard Bit)**: First bit shifted out beyond target precision.
- **$R$ (Round Bit)**: Second bit shifted out.
- **$S$ (Sticky Bit)**: Bitwise OR of all remaining shifted-out bits.

**Rounding Decision Rules**:
- If $G = 0$: Round down (truncate).
- If $G = 1$ and ($R = 1$ or $S = 1$): Round up (add 1 to LSB of significand).
- If $G = 1$ and $R = 0$ and $S = 0$ (Exact Tie):
  - If LSB of normalized significand is `1` (Odd): Round up to nearest even.
  - If LSB of normalized significand is `0` (Even): Round down.

---

## 12.4 Boundary Conditions & Edge Cases

| Boundary Condition | Expected Behavior |
| :--- | :--- |
| **Zero Significand** | Represented as $M = 0$ and $E = 0$. $0 + X = X$ and $0 \times X = 0$. |
| **Exact Power of Two** | $M$ has exactly 1 bit set. Alignment and normalization preserve bit position. |
| **Exponent Overflow** | Exponent exceeds `int.MaxValue`. Saturates or signals infinity state depending on context. |
| **Exponent Underflow** | Exponent drops below `int.MinValue`. Flushes to zero (FTZ) under denormal handling rules. |
| **Maximum Shift ($\Delta E \ge \text{Bits}$)** | Smaller operand becomes 0 with Sticky Bit $S = 1$ if any non-zero bits were shifted out. |
| **Sign Equivalence ($+0$ vs $-0$)** | Signed float $-0$ and $+0$ compare equal ($+0 == -0$). |

---

## 12.5 Preconditions & Invariants

1. **Unmanaged Type Constraint**: Type parameter `TSize` must be `unmanaged` to allow raw memory copy and GPU kernel execution.
2. **Static Alignment Invariant**: Field offsets for `Significand`, `Exponent`, and `IsNegative` are fixed at compile time.
3. **No Dynamic Allocation**: No `new` heap allocations occur inside device-side floating-point logic.
