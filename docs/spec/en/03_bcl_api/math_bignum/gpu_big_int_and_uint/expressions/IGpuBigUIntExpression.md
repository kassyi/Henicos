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


## Examples

```csharp
#nullable enable
using System;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

// 1. Setup GPU Context and inputs
using var context = new GpuContext(1024, GpuExecutionEngine.GSharpAot);
using var uintVal = new GpuUIntBigInteger(context, new uint[] { 42 });

// 2. Create the custom expression node
IGpuBigUIntExpression expression = new CustomUnsignedNode(uintVal);

// 3. Evaluate the expression
var result = expression.Evaluate(context);

Console.WriteLine($"Evaluated Words: {result.NumWords}");

// Create a mock AST node for Unsigned GPU BigInteger evaluation
public class CustomUnsignedNode : IGpuBigUIntExpression
{
    private readonly GpuUIntBigInteger _value;

    public CustomUnsignedNode(GpuUIntBigInteger value)
    {
        _value = value;
    }

    public int NumWords => _value.NumWords;

    public GpuUIntBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null)
    {
        // For demonstration, simply return the predefined value.
        return _value;
    }
}
```
