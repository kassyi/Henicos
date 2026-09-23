---
title: "4. BigNum Expression Trees Guide"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Detailed explanation of AST-based lazy evaluation, computational graphs, and how G# optimizes BigNum arithmetic."
---

# 4. BigNum Expression Trees Guide

To provide highly readable syntax without sacrificing GPU memory allocation performance (such as avoiding repeated `cudaMalloc` overheads per operation), `GpuBigInt` and `GpuBigFloat` employ a **Lazy Evaluation / Computational Graph** architecture on the host side.

Instead of computing immediately, operator overloads construct an AST (Abstract Syntax Tree), represented by `GpuBigIntExpr`.

---

## 4.1 Lazy Evaluation and AST

When you use operator overloads (`+`, `-`, `*`, `/`, `<<`, `>>`, `&`), the operation is not dispatched to the GPU immediately. Instead, a lightweight expression node is created on the host CPU.

**Example: Comprehensive Expression Tree**
```csharp
// Execution Context: Host (CPU)
// 1. Construct the AST (No GPU execution yet)
var expr = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;

// 2. Evaluate the tree and return the result as a new buffer
using var gpuResult = expr.Node.Evaluate(context, null);
```

This ensures that intermediate buffers can be efficiently reused, and the sequence of kernel dispatches can be pipelined optimally.

---

## 4.2 Diagnostic GSH0005: Preventing Computation Loss

Because operators only build an AST and do not execute any kernels natively, failing to evaluate the resulting expression leads to a silent "computation lost" bug (the GPU kernel is never dispatched).

To prevent this, the G# Roslyn Analyzer introduces **GSH0005** (`GpuBigIntExpr` Direct Assignment). This diagnostic produces a **hard compiler error** if a `GpuBigIntExpr` is declared or assigned to a local variable without being resolved or evaluated.

```csharp
// Execution Context: Host (CPU)
// ❌ INCORRECT: Triggers hard compiler error GSH0005
// var recipe = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;

// ✨ CORRECT: Enforced evaluation
var expr = (GpuBigIntExpr.Sqrt(gpuA + gpuB) / gpuC) & gpuA;
using var gpuResult = expr.Node.Evaluate(context, null);
```

---

## 4.3 Dynamic Sign Resolution (`SignResolutionKind`)

The standard library (`Kassyi.GSharp.Stdlib.Math`) has been enhanced to support runtime sign evaluation for the `GpuBigIntExpr` type without requiring kernel recompilation.

**Example**
```csharp
// Execution Context: Host (CPU)
using Kassyi.GSharp.Stdlib.Math;

public static void EvaluateBigInt(uint[] hostData, int dynamicValue)
{
    // 'a' has a dynamically resolved sign at runtime based on 'dynamicValue'
    GpuBigIntExpr a = GpuBigInt.FromInt32(dynamicValue);
    GpuBigIntExpr b = GpuBigInt.FromInt32(42); 
    
    // The multiplication seamlessly handles positive/negative permutations dynamically
    GpuBigIntExpr result = a * b;
}
```

## See also




### Architecture

- [2. Compiler and Runtime Architecture](../02_architecture/00_index.md)
- [4. BigNum (BigInt/BigFloat) Internal Architecture](../02_architecture/04_bignum_internals.md)

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../03_bcl_api/math_bignum/00_index.md)

