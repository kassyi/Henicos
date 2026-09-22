---
title: "Gpu.InlinePtx Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Injects raw PTX assembly directly into the generated kernel."
---

## Gpu.InlinePtx Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Injects raw PTX assembly directly into the generated kernel.

## Overloads

| Name | Description |
| --- | --- |
| [InlinePtx(string)](#inlineptx-string) | Injects raw PTX assembly directly into the generated kernel. |

## InlinePtx(string)

Injects raw PTX assembly directly into the generated kernel.

```csharp
[GpuKernel]
public static void InlinePtx(string ptxCode);
```

#### Parameters

`ptxCode` <i>string</i><br>
The PTX instruction string to embed.

## Examples

### Custom PTX Instructions

```csharp
// Execution Context: G# Kernel (GPU)
// Wait for a memory operation using raw PTX
Gpu.InlinePtx("membar.gl;");
```

## Remarks

This method allows utilizing low-level hardware features not explicitly covered by the G# BCL. Use with extreme caution.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
