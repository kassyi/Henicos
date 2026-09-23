---
title: "Arbitrary-Precision Floats (GpuBigFloat)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Overview of the GpuBigFloat module for arbitrary-precision floating-point arithmetic."
---

# Arbitrary-Precision Floats (GpuBigFloat)

The `GpuBigFloat` module provides high-performance, arbitrary-precision floating-point arithmetic on the GPU. It is designed for applications requiring precision far beyond standard IEEE 754 `float` or `double` (e.g., fractal generation, precise physics simulations).

> [!WARNING]
> **Host-Side Only API**  
> Just like `GpuBigInteger`, the classes and operator overloads in this module are designed exclusively for host-side orchestration. You cannot instantiate these types or call their operators inside a `[GpuKernel]`. Doing so will result in a transpiler compilation error (e.g., `GSH0001` or `RS0030`).

## Core Types

| Type | Description |
| :--- | :--- |
| [`GpuBigFloat`](GpuBigFloat.md) | Represents an arbitrary-precision floating-point number. Acts as a host-side wrapper orchestrating an unsigned mantissa and a signed exponent. |
| [`Operators (+, -, *, /)`](Operators.md) | Lazy-evaluated arithmetic operators for `GpuBigFloat`. They construct Abstract Syntax Trees (AST) for batch GPU execution. |

## Advanced Algorithms

> [!NOTE]
> Dedicated algorithm orchestrators for `GpuBigFloat` (e.g., Newton-Raphson Division, Taylor Series Trigonometry, Pi generation) are currently tracked for future implementation.

## See also



### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)

