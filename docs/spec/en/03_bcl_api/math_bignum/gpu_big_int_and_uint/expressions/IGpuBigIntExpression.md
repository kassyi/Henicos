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
| [Evaluate(GpuWorkspace, GpuUIntBigInteger?)](#evaluategpuworkspace-gpuuintbiginteger) | Evaluates this AST node into a new or provided GPU buffer, returning a signed `GpuBigInteger`. |

## Evaluate(GpuWorkspace, GpuUIntBigInteger?)

Evaluates this AST node into a new or provided GPU buffer, returning a signed `GpuBigInteger`.

```csharp
GpuBigInteger Evaluate(GpuWorkspace workspace, GpuUIntBigInteger? targetResult = null);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace for evaluation.

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

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
#nullable enable
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.Math;
using Kassyi.GSharp.Stdlib.Math.BigInt;
using Kassyi.GSharp.Stdlib.Math.BigInt.Expressions;

using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
using var aUint = new GpuUIntBigInteger(workspace, new uint[] { 10 });
using var bUint = new GpuUIntBigInteger(workspace, new uint[] { 25 });

var a = new GpuBigInteger(aUint, isNegative: false);
var b = new GpuBigInteger(bUint, isNegative: false);

IGpuBigIntExpression expression = new CustomSumNode(a, b);
var result = expression.Evaluate(workspace);

var resultWords = new uint[1];
workspace.Provider.CopyDeviceToHost(resultWords, result.Value.DevicePointer, 1);

Console.WriteLine($"Result: {(result.IsNegative ? "-" : "")}{resultWords[0]}");

public class CustomSumNode : IGpuBigIntExpression
{
    private readonly GpuBigInteger _left;
    private readonly GpuBigInteger _right;

    public CustomSumNode(GpuBigInteger left, GpuBigInteger right)
    {
        _left = left;
        _right = right;
    }

    public int NumWords => System.Math.Max(_left.Value.NumWords, _right.Value.NumWords) + 1;

    public GpuBigInteger Evaluate(GpuWorkspace workspace, GpuUIntBigInteger? targetResult = null)
    {
        var resultBuf = targetResult ?? new GpuUIntBigInteger(workspace, NumWords);
        using var leftPad = _left.Value.PadTo(NumWords);
        using var rightPad = _right.Value.PadTo(NumWords);
        GpuBigIntMath.Add(leftPad, rightPad, resultBuf);
        return new GpuBigInteger(resultBuf, isNegative: true);
    }
}
```
