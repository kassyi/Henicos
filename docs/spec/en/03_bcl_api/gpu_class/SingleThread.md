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
public static void SingleThread(Action body);
```

#### Parameters

`body`
`Action`
A delegate or lambda expressing the body to execute once.

## Examples

### Execution Guard

```csharp
[GpuKernel]
public static unsafe void InitializeMemory()
{
    Gpu.SingleThread(() => 
    {
        // Executes only on Thread 0, Block 0
        Gpu.Print("Kernel Started\n");
    });
}
```

## Remarks

Expands into an execution guard:
```cpp
if (threadIdx.x == 0 && blockIdx.x == 0) 
{ 
    // body...
}
```

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
