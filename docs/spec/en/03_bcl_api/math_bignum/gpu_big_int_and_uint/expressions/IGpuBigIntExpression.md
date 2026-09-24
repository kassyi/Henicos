---
title: "IGpuBigIntExpression Interface (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents a signed GPU BigInteger AST node."
---

# IGpuBigIntExpression Interface

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt.Expressions`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Represents a signed GPU BigInteger AST node.

## Properties

| Name                  | Description                                                                                          |
| --------------------- | ---------------------------------------------------------------------------------------------------- |
| [NumWords](#numwords) | Gets the number of words (32-bit units) required to store the absolute magnitude of this expression. |

## NumWords

Gets the number of words (32-bit units) required to store the absolute magnitude of this expression.

```csharp
int NumWords { get; }
```

## Methods

| Name                                                                              | Description                                                                                    |
| --------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| [Evaluate(GpuContext, GpuUIntBigInteger?)](#evaluategpucontext-gpuuintbiginteger) | Evaluates this AST node into a new or provided GPU buffer, returning a signed `GpuBigInteger`. |

## Evaluate(GpuContext, GpuUIntBigInteger?)

Evaluates this AST node into a new or provided GPU buffer, returning a signed `GpuBigInteger`.

```csharp
GpuBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null);
```

#### Parameters

`context` _GpuContext_<br>
The GPU context for evaluation.

`targetResult` _GpuUIntBigInteger?_<br>
An optional pre-allocated buffer for the absolute magnitude. If null, a temporary buffer is rented.

#### Returns

_GpuBigInteger_<br>
The evaluated result with its sign.

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
var signedVal = new GpuBigInteger(uintVal, isNegative: true);

// 2. Create the custom expression node
IGpuBigIntExpression expression = new CustomSignedNode(signedVal);

// 3. Evaluate the expression
var result = expression.Evaluate(context);

Console.WriteLine($"Evaluated Magnitude Words: {result.Value.NumWords}");
Console.WriteLine($"Is Negative: {result.IsNegative}");

// Example of a custom AST node for Signed GPU BigInteger evaluation
public class CustomSignedNode : IGpuBigIntExpression
{
    private readonly GpuBigInteger _value;

    public CustomSignedNode(GpuBigInteger value)
    {
        _value = value;
    }

    public int NumWords => _value.Value.NumWords;

    public GpuBigInteger Evaluate(GpuContext context, GpuUIntBigInteger? targetResult = null)
    {
        // For demonstration, simply return the predefined value.
        // A real AST node would evaluate an operation here.
        return _value;
    }
}
```
