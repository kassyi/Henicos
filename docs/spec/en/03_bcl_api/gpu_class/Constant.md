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

| Name | Description |
| --- | --- |
| [Constant(string)](#constant-string) | Directly injects a string literal as a raw C++ expression. |

## Constant(string)

Directly injects a string literal as a raw C++ expression.

```csharp
[GpuKernel]
public static int Constant(string expression);
```

#### Parameters

`expression` <i>string</i><br>
The string literal containing the C++ macro or preprocessor `#define` name.

#### Returns

<i>int</i><br>
Returns the specified type `int` (at compile-time, this forces the C# type checker to accept it, while the transpiler blindly pastes the string).

## Examples

### Preprocessor Inject

```csharp
// Execution Context: G# Kernel (GPU)
// Injects the C++ macro MAX_BUFFER_SIZE as an integer constant
int maxBufferSize = Gpu.Constant("MAX_BUFFER_SIZE");
```

## Remarks

This macro guarantees zero allocations at runtime. The transpiler directly substitutes the string literal into the resulting C++ AST.

## See also

### Related Attributes & Memory
- [ConstantAttribute](../attributes/ConstantAttribute.md)
- [Memory Architecture Index](../memory/00_index.md)
- [SharedMemory](../memory/SharedMemory.md)

### Language & Transpiler Architecture
- [3. Type System (Constants)](../../01_language/03_type_system.md#26-constants-const)
- [1. AOT Transpiler Architecture](../../02_architecture/01_aot_transpiler.md)
- [Gpu BCL API Overview](../00_overview.md)

### Performance Guides
- [Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
- [Antipatterns and Best Practices Guide](../../04_guides/06_antipatterns_and_best_practices.md)
