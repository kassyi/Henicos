---
title: "BigIntExpressionVisitor Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "A stack-scoped visitor that evaluates unsigned GPU BigInteger AST expressions using rented device memory buffers."
---

## BigIntExpressionVisitor Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A stack-scoped visitor (`ref struct`) that evaluates unsigned GPU BigInteger AST expressions (`IGpuBigUIntExpression`) using rented device memory buffers.

The first 16 levels of operator nodes are evaluated by recursion, the fastest form for an expression of ordinary depth; the levels below them are walked with an explicit stack instead of the call stack, so an expression built in a loop (`e = e + x` repeated) can be as deep as the loop, on any thread. The operands of a node are evaluated before the node, the left one first; the result buffer of a node is leased while its operands are alive, and the operands are returned right first, then left. A node implemented outside GpuMath evaluates itself through `Accept`; the operands it evaluates with `Visit` start a walk of their own. When an operation fails (a difference below zero or a `Fit` that does not fit, thrown at once under the CPU fallback), every buffer the evaluation leased goes back to the pool before the exception leaves.

```csharp
public ref struct BigIntExpressionVisitor(
    GpuBigIntExecutionContext context,
    ScopedBigIntMemory targetResult = default)
```

## Constructors

| Name | Description |
| --- | --- |
| [BigIntExpressionVisitor(GpuBigIntExecutionContext, ScopedBigIntMemory)](#bigintexpressionvisitorgpubigintexecutioncontext-scopedbigintmemory) | Initializes a new stack-scoped visitor with the specified execution context and optional target buffer. |

## BigIntExpressionVisitor(GpuBigIntExecutionContext, ScopedBigIntMemory)

Initializes a new stack-scoped visitor with the specified execution context and optional root destination buffer.

```csharp
public BigIntExpressionVisitor(
    GpuBigIntExecutionContext context,
    ScopedBigIntMemory targetResult = default);
```

#### Parameters

`context` _GpuBigIntExecutionContext_<br>
The execution context providing the memory manager and arithmetic providers.

`targetResult` _ScopedBigIntMemory_<br>
An optional pre-allocated destination buffer to receive the root evaluation result. When it overlaps an operand of the root, the root computes into a temporary instead and returns that; the caller copies it into the target. No arithmetic call receives a result that overlaps one of its operands. A root node with more words than the target throws `ArgumentException` (`ParamName` `targetResult`) before its kernel writes anything.

## Fields

| Name | Description |
| --- | --- |
| [Context](#context) | The execution context (`GpuBigIntExecutionContext`) for memory allocation and kernel dispatch. |

## Context

The execution context for memory allocation and kernel dispatch.

```csharp
public GpuBigIntExecutionContext Context;
```

## Methods

| Name | Description |
| --- | --- |
| [Visit(IGpuBigUIntExpression)](#visitigpubiguintexpression) | Visits and evaluates an arbitrary expression AST node. |
| [VisitGpuUIntBigInteger(GpuBigUInt)](#visitgpuuintbigintegergpubiguint) | Visits a leaf constant `GpuBigUInt` and wraps it in a non-owning scoped memory handle. |

## Visit(IGpuBigUIntExpression)

Visits and evaluates an arbitrary expression AST node.

```csharp
public ScopedBigIntMemory Visit(IGpuBigUIntExpression node);
```

#### Parameters

`node` _[IGpuBigUIntExpression](IGpuBigUIntExpression.md)_<br>
The expression node to visit.

#### Returns

_ScopedBigIntMemory_<br>
A scoped memory segment holding the evaluated result.

## VisitGpuUIntBigInteger(GpuBigUInt)

Visits a leaf constant `GpuBigUInt` and wraps it in a non-owning scoped memory handle.

```csharp
public ScopedBigIntMemory VisitGpuUIntBigInteger(GpuBigUInt node);
```

#### Parameters

`node` _[GpuBigUInt](../GpuBigUInt.md)_<br>
The GPU BigInteger node.

#### Returns

_ScopedBigIntMemory_<br>
A scoped memory segment referencing the `GpuBigUInt` buffer without transferring ownership.

## Examples

### Visiting a Leaf GPU BigInteger Expression Node

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
using var a = GpuBigUInt.FromBigInteger(workspace, 42, minWords: 4);

var execContext = new GpuBigIntExecutionContext(workspace, workspace.MemoryManager, workspace.Stream);
var visitor = new BigIntExpressionVisitor(execContext);
using ScopedBigIntMemory mem = visitor.VisitGpuUIntBigInteger(a);

Console.WriteLine($"Visited words: {mem.Handle.NumWords}");
```

## See also

### BCL API > Math Bignum

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [IGpuBigUIntExpression Interface](IGpuBigUIntExpression.md)
- [ScopedBigIntMemory](../providers/ScopedGpuMemory.md)
