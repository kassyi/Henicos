---
title: "3. Arbitrary-Precision GPU Integer Module (GpuBigInt)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "The `Kassyi.GSharp.GpuBigInt` module provides high-performance multi-precision arithmetic on the GPU. It is designed to handle extremely large inte..."
---

# 3. Arbitrary-Precision GPU Integer Module (GpuBigInt)

The `Kassyi.GSharp.GpuBigInt` module provides high-performance multi-precision arithmetic on the GPU. It is designed to handle extremely large integers (e.g., millions of bits).

> [!WARNING]
> **Host-Side Only API**: The `GpuBigInteger` class and its operator overloads are designed exclusively for host-side orchestration. You **cannot** use `+`, `-`, `*`, `/` or `Sqrt()` on `GpuBigInteger` objects from *inside* a `[GpuKernel]` method. All `GpuBigInt` arithmetic operations involve orchestrating multiple low-level CUDA kernels and workspace memory pools, which must be dispatched and managed from the CPU host.

---

## Part A: Host API Reference (Host-Only)

### `GpuContext` Class

Manages a thread-safe workspace pointer and pre-allocates intermediate buffers on the GPU to completely eliminate `cudaMalloc` overhead during execution.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public class GpuContext : IDisposable
{
    public GpuContext(int maxWords, GpuExecutionEngine engine = GpuExecutionEngine.GSharpAot);
    public void Dispose();
}
```

### `GpuUIntBigInteger` Class

A host-wrapper class (`IDisposable`) that manages the `IntPtr` to the GPU device memory for massive unsigned integers.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public class GpuUIntBigInteger : IDisposable
{
    public GpuUIntBigInteger(GpuContext context, int numWords);
    public GpuUIntBigInteger(GpuContext context, uint[] hostData);
    public uint[] ToHostArray();
    public static GpuUIntBigInteger FromBigInteger(GpuContext context, BigInteger value, int minWords = 0);
    public static GpuUIntBigInteger FromHexString(GpuContext context, string hexString, int minWords = 0);
    public BigInteger ToBigInteger();
    public string ToHexString();
    public void Dispose();
}
```

### `GpuBigInteger` Struct

A host-wrapper struct that pairs a `GpuUIntBigInteger` with a sign bit.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public readonly struct GpuBigInteger : IDisposable, IComparable<GpuBigInteger>
{
    public GpuBigInteger(GpuUIntBigInteger value, bool isNegative = false);
    public GpuUIntBigInteger Value { get; }
    public bool IsNegative { get; }
    public void Dispose();
}
```

---

## Part B: Supported Operations & Methods (Host-Only)

### Addition and Subtraction

Performs ripple-carry addition or borrow-based subtraction across multiple chunks.

**Operator Overloads**
**(Host-Only):** `GpuBigInt` provides `+` and `-` operator overloads designed exclusively for host-side orchestration. These overloads return a `GpuBigIntExpr` (Expression Tree) for lazy evaluation.

**Example**
```csharp
// Execution Context: Host (CPU)
var expr = gpuA + gpuB - gpuC;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### Multiplication

Highly optimized multiplication for massive sizes.

**Operator Overloads**
**(Host-Only):** The `*` operator overload is available exclusively on the host to construct a lazy-evaluated `GpuBigIntExpr`.

**Example**
```csharp
// Execution Context: Host (CPU)
var expr = gpuA * gpuB;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.Div` (Division)

Performs approximate division using Newton-Raphson iteration for reciprocals.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public static void Div(GpuBigInteger a, GpuBigInteger b, GpuBigInteger result);
```

**Remarks**
Due to iterative kernel dispatch overhead, GPU division is relatively slow for small numbers. It is recommended primarily for massive integers exceeding 1,024 words (32,768 bits). For smaller numbers, CPU-based division is generally faster.

### `GpuBigInteger.Sqrt` (Square Root)

Integer square root computation.

**Syntax**
```csharp
// Execution Context: Host (CPU)
// Note: Sqrt is only supported for unsigned BigIntegers.
public static GpuBigIntExpr Sqrt(GpuUIntBigInteger a);
```

**Example**
```csharp
// Execution Context: Host (CPU)
var expr = GpuBigInteger.Sqrt(gpuUIntA);
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.Pow10` (Power of 10)

Fast computation of $10^M$, optimized for arbitrary precision decimal scaling.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public static GpuBigIntExpr Pow10(int exponent);
```

**Example**
```csharp
// Execution Context: Host (CPU)
var expr = GpuBigInteger.Pow10(100); // Calculates 10^100
using var gpuResult = expr.Node.Evaluate(context, null);
```

### Bitwise Operations and Bit Shifts

Word-by-word bitwise AND, OR, XOR, NOT operations, and arbitrary bit shifts across the entire multi-precision array.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public static GpuBigIntExpr BitwiseAnd(GpuBigInteger a, GpuBigInteger b);
public static GpuBigIntExpr BitwiseOr(GpuBigInteger a, GpuBigInteger b);
public static GpuBigIntExpr BitwiseXor(GpuBigInteger a, GpuBigInteger b);
public static GpuBigIntExpr BitwiseNot(GpuBigInteger a);
public static GpuBigIntExpr ShiftLeft(GpuBigInteger a, int shiftBits);
public static GpuBigIntExpr ShiftRight(GpuBigInteger a, int shiftBits);
```

**Example**
```csharp
// Execution Context: Host (CPU)
// Easily composed via standard C# operators using Expression Trees
var expr = (~gpuA & gpuB) << 256;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.Gcd` (Greatest Common Divisor)

Multi-precision Greatest Common Divisor computation.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public static GpuBigIntExpr Gcd(GpuBigInteger a, GpuBigInteger b);
```

**Example**
```csharp
// Execution Context: Host (CPU)
var expr = GpuBigInteger.Gcd(gpuA, gpuB);
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.ModPow` (Modular Exponentiation)

Binary exponentiation $a^b \pmod m$ on GPU.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public static GpuBigIntExpr ModPow(GpuBigInteger a, GpuBigInteger exponent, GpuBigInteger modulus);
```

**Example**
```csharp
// Execution Context: Host (CPU)
var expr = GpuBigInteger.ModPow(gpuA, exponent, modulus);
using var gpuResult = expr.Node.Evaluate(context, null);
```

### `GpuBigInteger.Square` (Instance Method)

Highly optimized in-place squaring.

**Syntax**
```csharp
// Execution Context: Host (CPU)
public void Square(int precisionWords);
```

**Remarks**
This is an in-place mutation operation. For extremely large integers, calling `Square()` directly is significantly faster than using the standard multiplication operator (`A * A`) via expression trees.

**Example**
```csharp
// Execution Context: Host (CPU)
gpuA.Value.Square(1024); // Squares gpuA's underlying unsigned value in place
```

---

## Part C: Operator Overloading & Expression Trees (Host-Only)

To provide highly readable syntax without sacrificing GPU memory allocation performance (`cudaMalloc` overhead), `GpuBigInt` employs a **Lazy Evaluation / Computational Graph** architecture.

Instead of computing immediately, operator overloads (`+`, `-`, `*`, `/`, `<<`, `>>`, `&`) construct an AST (Abstract Syntax Tree).

### Supported Operator Overloads
* **Arithmetic**: `+`, `-`, `*`, `/`
* **Bitwise**: `&`, `|`, `^`, `~`
* **Bit Shifts**: `<<`, `>>`

**Example: Comprehensive Expression Tree**
```csharp
// Execution Context: Host (CPU)
// Evaluate the tree and return the result as a new buffer
var expr = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### Diagnostic GSH0005: Preventing Computation Loss

Because operators only build an AST and do not execute any kernels natively, failing to evaluate the resulting expression leads to a silent "computation lost" bug (the GPU kernel is never dispatched).

To prevent this, the G# Roslyn Analyzer introduces `GSH0005` (GpuBigIntExpr Direct Assignment). This diagnostic produces a **hard compiler error** if a `GpuBigIntExpr` is declared or assigned to a local variable without being resolved or evaluated.

```csharp
// Execution Context: Host (CPU)
// ❌ INCORRECT: Triggers hard compiler error GSH0005
// var recipe = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;

// ✨ CORRECT: Enforced evaluation
var expr = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;
using var gpuResult = expr.Node.Evaluate(context, null);
```

### Dynamic Sign Resolution (`SignResolutionKind`)

The standard library (`Kassyi.GSharp.Stdlib.Math`) has been enhanced to support runtime sign evaluation for the `GpuBigIntExpr` type without requiring kernel recompilation.

**Example**
```csharp
// Execution Context: G# Kernel (GPU)
using Kassyi.GSharp.Stdlib.Math;

[GpuKernel]
public static void EvaluateBigInt(uint* data, int dynamicValue)
{
    // 'a' has a dynamically resolved sign at runtime based on 'dynamicValue'
    GpuBigIntExpr a = GpuBigInt.FromInt32(dynamicValue);
    GpuBigIntExpr b = GpuBigInt.FromInt32(42); 
    
    // The multiplication seamlessly handles positive/negative permutations dynamically
    GpuBigIntExpr result = a * b;
}
```

---

## Part D: Architecture and Implementation Details (Advanced)

For deep internal architecture details regarding the multi-engine execution model, algorithmic complexity, and memory safety invariants, refer to:
**[BigNum Internals](../02_architecture/04_bignum_internals.md)**