---
title: "Gpu.ForUnroll Method (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Transpilation macro that unrolls a loop body at compile-time by duplicating the source code across the specified range."
---

## Gpu.ForUnroll Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Transpilation macro that unrolls a loop body at compile-time by duplicating the source code across the specified range.

## Overloads

| Name | Description |
| --- | --- |
| [ForUnroll(int, int, Action\<int\>)](#forunrollint-int-actionint) | Unrolls a loop body at compile-time by duplicating the source code across the specified range. |
| [ForUnroll\<TContext\>(ref TContext, int, int, Gpu.UnrollAction\<TContext\>)](#forunrolltcontextref-tcontext-int-int-gpuunrollactiontcontext) | Unrolls a loop body at compile-time with context state propagation across iterations. |

## ForUnroll(int, int, Action\<int\>)

Transpilation macro that unrolls a loop body at compile-time by duplicating the source code across the specified range.

```csharp
public static void ForUnroll(int start, int count, Action<int> body);
```

#### Parameters

`start` _int_<br>
The constant loop starting index.

`count` _int_<br>
The constant loop iteration count.

`body` _Action\<int\>_<br>
The loop body delegate receiving the zero-based unrolled loop iteration index.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## ForUnroll\<TContext\>(ref TContext, int, int, Gpu.UnrollAction\<TContext\>)

Transpilation macro that unrolls a loop body at compile-time with context state propagation.

```csharp
public delegate void UnrollAction<TContext>(ref TContext ctx, int index);

public static void ForUnroll<TContext>(
    ref TContext ctx,
    int start,
    int count,
    Gpu.UnrollAction<TContext> body);
```

#### Type Parameters

`TContext`<br>
The state structure type passed by reference across loop iterations.

#### Parameters

`ctx` _TContext_<br>
The execution context passed by reference.

`start` _int_<br>
The constant loop starting index.

`count` _int_<br>
The constant loop iteration count.

`body` _Gpu.UnrollAction\<TContext\>_<br>
The loop body delegate receiving the context by reference and the iteration index.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Examples

### Compile-Time Unrolled Loop

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU)
try
{
    int accumulator = 0;
    Gpu.ForUnroll(ref accumulator, 0, 4, (ref int acc, int i) =>
    {
        acc += i;
    });
}
catch (GpuOnlyException)
{
    Console.WriteLine("Gpu.ForUnroll is a compile-time GPU transpilation macro.");
}
```

## Remarks

This method should be used strictly when the loop variable needs to be evaluated as a compile-time constant literal (e.g., `0`, `1`, `2`). This is essential when passing the index to methods that enforce strict constant evaluation.

> [!CAUTION]
> Because this macro performs literal source code duplication during transpilation, using it for loops with high iteration counts will drastically bloat the generated C++ code. For general performance tuning and Instruction-Level Parallelism (ILP) improvements, use [`Gpu.Hint.Unroll`](HintUnroll.md) instead.

## See also

### BCL API > Gpu Class

- [Gpu Class](00_index.md)
- [Gpu.Hint.Unroll Method (G#)](HintUnroll.md)
- [Gpu.GridStrideLoop Method (G#)](GridStrideLoop.md)
