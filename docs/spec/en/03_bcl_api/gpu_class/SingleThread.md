---
title: "Gpu.SingleThread Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Wraps the provided body in a thread execution guard."
---

## Gpu.SingleThread Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Wraps the provided body in a thread execution guard.

## Overloads

| Name | Description |
| --- | --- |
| [SingleThread(Action)](#singlethread-action) | Wraps the provided body in a thread execution guard. |

## SingleThread(Action)

Wraps the provided body in a thread execution guard.

```csharp
[GpuKernel]
public static void SingleThread(Action body);
```

#### Parameters

`body` <i>Action</i><br>
A delegate or lambda expressing the body to execute once.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Examples

### Execution Guard

```csharp
// Execution Context: G# Kernel (GPU)
using System;
using Kassyi.GSharp.Core;

try {
    // Ensures the enclosed block only runs on thread 0 of block 0
    Gpu.SingleThread(() => 
    {
        Console.WriteLine("Executing exclusively on Thread 0, Block 0.");
    });
} catch (GpuOnlyException) {
    Console.WriteLine("Gpu.SingleThread can only be executed on the GPU.");
}
```

## Remarks

Expands into an execution guard:
```cpp
// Execution Context: Generated C++ (GPU)
if (threadIdx.x == 0 && blockIdx.x == 0) 
{ 
    // body...
}
```

## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)

### BCL API > Gpu Class

- [Gpu Class](00_index.md)
- [Gpu.GridStrideLoop Method (G#)](GridStrideLoop.md)

