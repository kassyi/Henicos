---
title: "Gpu.Constant Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Directly injects a string literal as a raw C++ expression."
---

## Gpu.Constant Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Directly injects a string literal as a raw C++ expression.

## Overloads

| Name                                       | Description                                                |
| ------------------------------------------ | ---------------------------------------------------------- |
| [Constant&lt;T&gt;(string)](#constanttstring) | Directly injects a string literal as a raw C++ expression. |

## Constant&lt;T&gt;(string)

Directly injects a string literal as a raw C++ expression.

```csharp
[GpuKernel]
public static T Constant<T>(string expression);
```

#### Type Parameters

`T`<br>
The expected type of the constant placeholder (e.g., `int`, `float`).

#### Parameters

`expression` <i>string</i><br>
The string literal containing the C++ macro or preprocessor `#define` name.

#### Returns

<i>T</i><br>
Returns the specified placeholder type `T` (at compile-time, this satisfies the C# type checker, while the transpiler blindly substitutes the identifier into the C++ AST).

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Examples

### Preprocessor Inject

```csharp
// Execution Context: G# Kernel (GPU)
using System;
using Kassyi.GSharp.Core;

try {
    // Replaced with literal macro value during GPU runtime compilation
    int blockSize = Gpu.Constant<int>("BLOCK_SIZE");
    float pi = Gpu.Constant<float>("PI_CONST");
    Console.WriteLine($"Constant evaluated to: {blockSize}, {pi}");
} catch (GpuOnlyException) {
    Console.WriteLine("Gpu.Constant can only be executed on the GPU.");
}
```

## Remarks

This macro guarantees zero allocations at runtime. The transpiler directly substitutes the string literal into the resulting C++ AST.

## See also

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### BCL API > Attributes

- [Attributes](../attributes/00_index.md)
- [ConstantAttribute Class (G#)](../attributes/ConstantAttribute.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
