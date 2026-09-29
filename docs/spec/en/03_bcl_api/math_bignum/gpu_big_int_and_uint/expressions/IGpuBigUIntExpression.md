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
| [Evaluate(GpuWorkspace, GpuUIntBigInteger?)](#evaluategpuworkspace-gpuuintbiginteger) | Evaluates this expression on the GPU into the specified target buffer. |

## Evaluate(GpuWorkspace, GpuUIntBigInteger?)

Evaluates this expression on the GPU into the specified target buffer.

```csharp
GpuUIntBigInteger Evaluate(GpuWorkspace workspace, GpuUIntBigInteger? targetResult = null);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
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
using var left = new GpuUIntBigInteger(workspace, new uint[] { 500 });
using var right = new GpuUIntBigInteger(workspace, new uint[] { 700 });

IGpuBigUIntExpression expression = new CustomAddNode(left, right);
using var result = expression.Evaluate(workspace);

var resultData = new uint[1];
workspace.Provider.CopyDeviceToHost(resultData, result.DevicePointer, 1);

Console.WriteLine($"500 + 700 = {resultData[0]}");

public class CustomAddNode : IGpuBigUIntExpression
{
    private readonly GpuUIntBigInteger _left;
    private readonly GpuUIntBigInteger _right;

    public CustomAddNode(GpuUIntBigInteger left, GpuUIntBigInteger right)
    {
        _left = left;
        _right = right;
    }

    public int NumWords => System.Math.Max(_left.NumWords, _right.NumWords) + 1;

    public GpuUIntBigInteger Evaluate(GpuWorkspace workspace, GpuUIntBigInteger? targetResult = null)
    {
        var resultBuf = targetResult ?? new GpuUIntBigInteger(workspace, NumWords);
        workspace.Provider.Add(workspace, _left, _right, resultBuf);
        return resultBuf;
    }
}
```
