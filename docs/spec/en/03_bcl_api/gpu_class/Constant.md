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
public static T Constant<T>(string expression);
```

#### Parameters

`expression`
`string`
The string literal containing the C++ macro or preprocessor `#define` name.

#### Returns

`int`
Returns the specified type `int` (at compile-time, this forces the C# type checker to accept it, while the transpiler blindly pastes the string).

## Examples

### Preprocessor Inject

```csharp
// Injects the C++ macro MAX_BUFFER_SIZE as an integer constant
int maxBufferSize = Gpu.Constant<int>("MAX_BUFFER_SIZE");
```

## Remarks

This macro guarantees zero allocations at runtime. The transpiler directly substitutes the string literal into the resulting C++ AST.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
