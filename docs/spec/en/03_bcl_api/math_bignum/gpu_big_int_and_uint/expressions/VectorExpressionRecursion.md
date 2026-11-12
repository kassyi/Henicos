---
title: "VectorExpressionRecursion Class (G#)"
site: "G# Documentation"
published: "2026-11-11T00:00:00.000Z"
language: "en"
description: "The low-level evaluation of vector expressions by plain recursion on the caller's stack, for shallow expressions."
---

## VectorExpressionRecursion Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

The low-level evaluation of vector expressions: by plain recursion on the caller's stack, one call stack frame per
level of vector nodes, for an expression the caller knows to be shallow.

```csharp
public static class VectorExpressionRecursion
```

## Remarks

`Evaluate` of a vector expression evaluates any depth: it recurses through the first levels and walks the levels below
them with explicit stacks, which costs a few tens of nanoseconds of host time per node. `EvaluateOnCallStack` recurses
through every level instead and supports at most `MaxCallStackDepth` (64) levels of vector nodes. A level takes a few
kilobytes of stack, so the supported depth fits in about 256 KB, inside the default stack of a thread (1 MB on Windows,
8 MB on Linux).

An expression deeper than `MaxCallStackDepth` levels throws `NotSupportedException` when the evaluation reaches the
first node below that depth; the nodes evaluated before it keep their buffers, as after any failed evaluation. The
results, the order of evaluation and the checks are those of `Evaluate`; so are the target, the window and the
lifetime of the returned view.

## Fields

| Name | Description |
| :- | :- |
| `MaxCallStackDepth` | The most levels of vector nodes evaluated by recursion on the caller's stack: 64. |

## Methods

| Name | Description |
| :- | :- |
| `EvaluateOnCallStack(IGpuBigIntVectorExpression, GpuWorkspace, Int32, Int32?, GpuBigUIntArrayView)` | Evaluates an integer vector expression by recursion; the result of `Evaluate`. |
| `EvaluateOnCallStack(IGpuBigFloatVectorExpression, GpuWorkspace, Int32, Int32?, GpuBigFloatArrayView)` | Evaluates a floating-point vector expression by recursion; the result of `Evaluate`. |
| `EvaluateOnCallStack(IGpuBoolVectorExpression, GpuWorkspace)` | Evaluates a flag vector expression by recursion; the result of `Evaluate`. |

All three throw `NotSupportedException` for an expression deeper than `MaxCallStackDepth` levels and under the CPU
fallback.

## Examples

```csharp
// An expression of three nodes, evaluated without the walk
using var sum = a.AsVectorizedEnumerable().SelectAdd(s).ZipAdd(b.AsVectorizedEnumerable());
var view = sum.EvaluateOnCallStack(workspace);
```

## See also

### BCL API > Math Bignum

- [IGpuBigIntVectorExpression Interface](IGpuBigIntVectorExpression.md)
- [IGpuBoolVectorExpression Interface](IGpuBoolVectorExpression.md)
- [GpuBigIntVectorLinqExtensions Class](GpuBigIntVectorLinqExtensions.md)
