---
title: "Gpu.SingleThread Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Runs the provided body once per launch, on the thread whose thread and block indices are all 0."
---

## Gpu.SingleThread Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Runs the provided body once per launch, on the thread whose thread and block indices are all 0.

## Overloads

| Name | Description |
| --- | --- |
| [SingleThread(Action)](#singlethread-action) | Runs the provided body once per launch. |

## SingleThread(Action)

Runs the provided body once per launch, on the thread whose thread and block indices are all 0.

```csharp
public static void SingleThread(Action body);
```

#### Parameters

`body` _Action_<br>
A delegate or lambda expressing the body to execute once.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU outside a kernel launch.

## Examples

### Execution Guard

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU)

try {
    // Ensures the enclosed block runs once, on thread (0,0,0) of block (0,0,0)
    Gpu.SingleThread(() => 
    {
        Console.WriteLine("Executing exclusively on thread (0,0,0) of block (0,0,0).");
    });
} catch (GpuOnlyException) {
    Console.WriteLine("Gpu.SingleThread can only be executed on the GPU.");
}
```

## Remarks

The body runs exactly once per launch, whatever the shape of the blocks and the grid: only the thread whose six indices
(`threadIdx.x`, `.y`, `.z` and `blockIdx.x`, `.y`, `.z`) are all 0 runs it. It expands into an execution guard:

```cpp
// Execution Context: Generated C++ (GPU)
if((threadIdx.x|threadIdx.y|threadIdx.z|blockIdx.x|blockIdx.y|blockIdx.z)==0)
{
    // body...
}
```

CUDA, the native CPU path and the managed CPU path use the same condition.

A `return` in the body leaves the body only, as in C#: the statements after `Gpu.SingleThread` still run on that thread.
The generated code jumps to a label after the guard instead of returning from the kernel:

```csharp
// Execution Context: G# Kernel (GPU)
Gpu.SingleThread(() =>
{
    if (flag == 0)
    {
        return;      // leaves the body
    }

    a[0] = 1;
});
a[ThreadIndex.X + 1] = 2;  // thread (0,0,0) of block (0,0,0) still runs this
```

## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)

### BCL API > Gpu Class

- [Gpu Class](00_index.md)
- [Gpu.GridStrideLoop Method (G#)](GridStrideLoop.md)

