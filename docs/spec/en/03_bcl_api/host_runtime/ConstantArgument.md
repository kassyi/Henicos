---
title: "ConstantArgument Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents a key-value constant argument passed to kernel compilation without heap allocation or boxing."
---

## ConstantArgument Struct

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Aot`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Represents a key-value constant argument passed to kernel compilation without heap allocation or boxing.

```csharp
public enum ConstantType : byte
{
    Int32,
    UInt32,
    Int64,
    UInt64,
    Single,
    Double,
    Boolean
}

[StructLayout(LayoutKind.Explicit)]
public struct ConstantValue
{
    [FieldOffset(0)] public int Int32Value;
    [FieldOffset(0)] public uint UInt32Value;
    [FieldOffset(0)] public long Int64Value;
    [FieldOffset(0)] public ulong UInt64Value;
    [FieldOffset(0)] public float SingleValue;
    [FieldOffset(0)] public double DoubleValue;
    [FieldOffset(0)] public bool BooleanValue;
}

public readonly struct ConstantArgument
```

## Fields

| Name | Description |
| --- | --- |
| `Key` | Gets the name of the constant template or macro parameter. |
| `Type` | Gets the `ConstantType` discriminator of the constant argument. |
| `Value` | Gets the unboxed `ConstantValue` union of the constant argument. |

## Methods

| Name | Description |
| --- | --- |
| [Create(string, int)](#createstring-int) | Creates a new constant argument for a 32-bit signed integer. |
| [Create(string, uint)](#createstring-uint) | Creates a new constant argument for a 32-bit unsigned integer. |
| [Create(string, long)](#createstring-long) | Creates a new constant argument for a 64-bit signed integer. |
| [Create(string, ulong)](#createstring-ulong) | Creates a new constant argument for a 64-bit unsigned integer. |
| [Create(string, float)](#createstring-float) | Creates a new constant argument for a single-precision floating-point number. |
| [Create(string, double)](#createstring-double) | Creates a new constant argument for a double-precision floating-point number. |
| [Create(string, bool)](#createstring-bool) | Creates a new constant argument for a boolean value. |
| [GetStringValue()](#getstringvalue) | Converts the constant argument to its string representation used during C++/CUDA code generation. |

## Create(string, int)

Creates a new constant argument for a 32-bit signed integer.

```csharp
public static ConstantArgument Create(string key, int value);
```

#### Parameters

`key` _string_<br>
The parameter name.

`value` _int_<br>
The 32-bit signed integer value.

#### Returns

_ConstantArgument_<br>
A new `ConstantArgument` instance.

## Create(string, uint)

Creates a new constant argument for a 32-bit unsigned integer.

```csharp
public static ConstantArgument Create(string key, uint value);
```

#### Parameters

`key` _string_<br>
The parameter name.

`value` _uint_<br>
The 32-bit unsigned integer value.

#### Returns

_ConstantArgument_<br>
A new `ConstantArgument` instance.

## Create(string, long)

Creates a new constant argument for a 64-bit signed integer.

```csharp
public static ConstantArgument Create(string key, long value);
```

#### Parameters

`key` _string_<br>
The parameter name.

`value` _long_<br>
The 64-bit signed integer value.

#### Returns

_ConstantArgument_<br>
A new `ConstantArgument` instance.

## Create(string, ulong)

Creates a new constant argument for a 64-bit unsigned integer.

```csharp
public static ConstantArgument Create(string key, ulong value);
```

#### Parameters

`key` _string_<br>
The parameter name.

`value` _ulong_<br>
The 64-bit unsigned integer value.

#### Returns

_ConstantArgument_<br>
A new `ConstantArgument` instance.

## Create(string, float)

Creates a new constant argument for a single-precision floating-point number.

```csharp
public static ConstantArgument Create(string key, float value);
```

#### Parameters

`key` _string_<br>
The parameter name.

`value` _float_<br>
The single-precision floating-point value.

#### Returns

_ConstantArgument_<br>
A new `ConstantArgument` instance.

## Create(string, double)

Creates a new constant argument for a double-precision floating-point number.

```csharp
public static ConstantArgument Create(string key, double value);
```

#### Parameters

`key` _string_<br>
The parameter name.

`value` _double_<br>
The double-precision floating-point value.

#### Returns

_ConstantArgument_<br>
A new `ConstantArgument` instance.

## Create(string, bool)

Creates a new constant argument for a boolean value.

```csharp
public static ConstantArgument Create(string key, bool value);
```

#### Parameters

`key` _string_<br>
The parameter name.

`value` _bool_<br>
The boolean value.

#### Returns

_ConstantArgument_<br>
A new `ConstantArgument` instance.

## GetStringValue()

Converts the constant argument to its string representation used during code generation.

```csharp
public string GetStringValue();
```

#### Returns

_string_<br>
A string representation formatted for CUDA/C++ template or `#define` substitution.

## Examples

### Creating Unboxed Constant Arguments

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Aot;

// Execution Context: Host (CPU)

var blockSizeArg = ConstantArgument.Create("BLOCK_SIZE", 256);
var flagArg = ConstantArgument.Create("USE_FAST_PATH", true);

Console.WriteLine($"{blockSizeArg.Key} ({blockSizeArg.Type}) = {blockSizeArg.GetStringValue()}");
Console.WriteLine($"{flagArg.Key} ({flagArg.Type}) = {flagArg.GetStringValue()}");
```

## Remarks

`ConstantArgument` uses an explicit-layout union (`ConstantValue`) to store primitive constant values on the stack without boxing `System.ValueType` instances on the managed heap.

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [GpuKernelLoader Class (G#)](GpuKernelLoader.md)

### BCL API > Attributes

- [ConstantAttribute Class (G#)](../attributes/ConstantAttribute.md)
