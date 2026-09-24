---
title: "IGpuBigUIntExpression Interface (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated GPU BigInteger expression."
---

# IGpuBigUIntExpression Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Defines the contract for an abstract syntax tree (AST) node representing a lazily evaluated GPU BigInteger expression.

## Properties

| Name                  | Description                                                       |
| --------------------- | ----------------------------------------------------------------- |
| [NumWords](#numwords) | Gets the word length (limb count) resulting from this expression. |

## NumWords

Gets the word length (limb count) resulting from this expression.

```csharp
int NumWords { get; }
```

## Methods

| Name                                                                              | Description                                                            |
| --------------------------------------------------------------------------------- | ---------------------------------------------------------------------- |
| [Evaluate(GpuContext, GpuUIntBigInteger?)](#evaluategpucontext-gpuuintbiginteger) | Evaluates this expression on the GPU into the specified target buffer. |

## Evaluate(GpuContext, GpuUIntBigInteger?)

Evaluates this expression on the GPU into the specified target buffer.

```csharp
GpuUIntBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null);
```

#### Parameters

`context` _GpuContext_<br>
The active GPU execution context.

`targetResult` _GpuUIntBigInteger?_<br>
Optional target buffer to write the evaluated result into.

#### Returns

_GpuUIntBigInteger_<br>
The evaluated `GpuUIntBigInteger` result buffer.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../../00_index.md)
- [Arbitrary-Precision Integers (GpuBigInteger)](../00_index.md)
