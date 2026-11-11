---
title: "4. BigNum Expression Trees Guide"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Detailed explanation of AST-based lazy evaluation, computational graphs, and how G# optimizes BigNum arithmetic."
---

# 4. BigNum Expression Trees Guide

To provide highly readable syntax without sacrificing GPU memory allocation performance (such as avoiding repeated `cudaMalloc` overheads per operation), `GpuBigInt` and `GpuBigFloat` employ a **Lazy Evaluation / Computational Graph** architecture on the host side. Note that `GpuBigFloat` and `GpuBigUFloat` are non-generic structs; generic parameterizations and fictitious fixed-precision float wrappers (such as GpuFloat128) do not exist.

Instead of computing immediately, operator overloads construct an AST (Abstract Syntax Tree), represented by `GpuBigIntExpr`.

---

## 4.1 Lazy Evaluation and AST

When you use operator overloads (`+`, `-`, `*`, `/`, `<<`, `>>`, `&`), the operation is not dispatched to the GPU immediately. Instead, a lightweight expression node is created on the host CPU.

> [!WARNING]
> **Host-Side Allocations Occur Here**
> Although GSharp enforces a strict zero-allocation policy on the GPU device and avoids `cudaMalloc` by using `GpuWorkspace` pooling, constructing this Abstract Syntax Tree (AST) via operator overloads *does* allocate lightweight `class` nodes on the managed .NET heap. If you are operating in an extreme high-frequency loop on the CPU, building the expression tree itself will generate garbage.

**Example: Comprehensive Expression Tree**
```csharp
// Execution Context: Host (CPU)
// GpuBigInt wraps a GpuBigUInt buffer with a sign flag (primary constructor is (GpuBigUInt, bool)):
using var workspace = new GpuWorkspace(1024);
using var uA = GpuBigUInt.FromBigInteger(workspace, 100);
using var gpuA = new GpuBigInt(uA, isNegative: false);
using var uB = GpuBigUInt.FromBigInteger(workspace, 50);
using var gpuB = new GpuBigInt(uB, isNegative: true);
using var uC = GpuBigUInt.FromBigInteger(workspace, 2);
using var gpuC = new GpuBigInt(uC, isNegative: false);

// Evaluate the expression tree directly into a result buffer without declaring intermediate GpuBigIntExpr locals:
using var gpuResult = ((gpuA + gpuB) * gpuC - gpuA).Evaluate(workspace);
```

This ensures that intermediate buffers can be efficiently reused, and the sequence of kernel dispatches can be pipelined optimally.

---

## 4.2 Diagnostic GSH0005: Preventing Computation Loss

Because operators only build an AST and do not execute any kernels natively, failing to evaluate the resulting expression leads to a silent "computation lost" bug (the GPU kernel is never dispatched).

To prevent this, the G# Roslyn Analyzer introduces **GSH0005** (`GpuBigIntExpr` Direct Assignment). This diagnostic produces a **hard compiler error** if a `GpuBigIntExpr` is declared or assigned to a local variable without being resolved or evaluated.

```csharp
// Execution Context: Host (CPU)
// ❌ INCORRECT: Triggers hard compiler error GSH0005
// GpuBigIntExpr recipe = (gpuA + gpuB) * gpuC - gpuA;

// ✨ CORRECT: Enforced evaluation
using var gpuResult = ((gpuA + gpuB) * gpuC - gpuA).Evaluate(workspace);
```

---

## 4.3 Dynamic Sign Resolution (`SignResolutionKind`)

The standard library (`Kassyi.GSharp.Stdlib.GpuMath`) has been enhanced to support runtime sign evaluation for the `GpuBigIntExpr` type without requiring kernel recompilation.

**Example**
```csharp
// Execution Context: Host (CPU)
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

public static void EvaluateBigInt(GpuWorkspace workspace, int dynamicValue)
{
    using var uA = GpuBigUInt.FromBigInteger(workspace, BigInteger.Abs(dynamicValue));
    using var gpuA = new GpuBigInt(uA, dynamicValue < 0);

    using var uB = GpuBigUInt.FromBigInteger(workspace, 42);
    using var gpuB = new GpuBigInt(uB, isNegative: false);

    using var result = (gpuA * gpuB).Evaluate(workspace);
}
```

## See also




### Architecture

- [2. Compiler and Runtime Architecture](../02_architecture/00_index.md)
- [4. BigNum (BigInt/BigFloat) Internal Architecture](../02_architecture/04_bignum_internals.md)

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../03_bcl_api/math_bignum/00_index.md)

