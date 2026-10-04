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

| Name                                   | Description                                                  |
| -------------------------------------- | ------------------------------------------------------------ |
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

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Examples

### Custom PTX Instructions

```csharp
// Execution Context: G# Kernel (GPU)
using System;
using Kassyi.GSharp.Core;

try {
    // Injects raw PTX assembly directly into the transpiled C++ output
    Gpu.InlinePtx("asm volatile(\"membar.gl;\");");
    Console.WriteLine("Inline PTX injected.");
} catch (GpuOnlyException) {
    Console.WriteLine("Gpu.InlinePtx can only be executed on the GPU.");
}
```

## Remarks

This method allows utilizing low-level hardware features not explicitly covered by the G# BCL. Use with extreme caution.

## See also

### Architecture

- [2. Compiler and Runtime Architecture](../../02_architecture/00_index.md)
- [1. Generator Architecture (`Kassyi.GSharp.Compiler.Generators`)](../../02_architecture/01_aot_transpiler.md)

### BCL API > Attributes

- [Attributes](../attributes/00_index.md)
- [Kassyi.GSharp.Core: Advanced Mapping Attributes](../attributes/AdvancedMappingAttributes.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../04_guides/08_advanced_custom_math_kernels.md)
