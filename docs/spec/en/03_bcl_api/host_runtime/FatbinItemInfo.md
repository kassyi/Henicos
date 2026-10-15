---
title: "FatbinItemInfo Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents metadata for an embedded module (cuBIN or PTX) within an NVIDIA FATBIN binary."
---

## FatbinItemInfo Struct

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Fatbin`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Represents metadata for an embedded module (cuBIN or PTX) within an NVIDIA FATBIN binary.

```csharp
public readonly struct FatbinItemInfo
```

## Fields

| Name | Description |
| --- | --- |
| [TypeCubin](#typecubin) | Type identifier for native ELF / cuBIN machine code modules (`2`). |
| [TypeElf](#typeelf) | Alias for `TypeCubin` representing ELF binary modules (`2`). |
| [TypePtx](#typeptx) | Type identifier for PTX intermediate assembly modules (`1`). |

## TypeCubin

Type identifier for native ELF / cuBIN machine code modules (`2`).

```csharp
public const ushort TypeCubin = 2;
```

## TypeElf

Alias for `TypeCubin` representing ELF binary modules (`2`).

```csharp
public const ushort TypeElf = 2;
```

## TypePtx

Type identifier for PTX intermediate assembly modules (`1`).

```csharp
public const ushort TypePtx = 1;
```

## Constructors

| Name | Description |
| --- | --- |
| [FatbinItemInfo(UInt16, UInt16, UInt16, UInt32, String, UInt64)](#fatbiniteminfouint16-uint16-uint16-uint32-string-uint64) | Initializes a new instance of the `FatbinItemInfo` struct. |

## FatbinItemInfo(UInt16, UInt16, UInt16, UInt32, String, UInt64)

Initializes a new instance of the `FatbinItemInfo` struct.

```csharp
public FatbinItemInfo(
    ushort type,
    ushort major,
    ushort minor,
    uint computeVersion,
    string? objectName,
    ulong dataSize);
```

#### Parameters

`type` _UInt16_<br>
The type of embedded module (`1` = PTX, `2` = ELF/cuBIN).

`major` _UInt16_<br>
The architecture major version.

`minor` _UInt16_<br>
The architecture minor version.

`computeVersion` _UInt32_<br>
The compute capability version number.

`objectName` _String_<br>
The optional object name identifier.

`dataSize` _UInt64_<br>
The size in bytes of the embedded module data payload.

## Properties

| Name | Description |
| --- | --- |
| [ComputeVersion](#computeversion) | Gets the raw compute capability version (e.g., `80` for `sm_80`, `86` for `sm_86`). |
| [DataSize](#datasize) | Gets the size in bytes of the embedded module data payload. |
| [Major](#major) | Gets the architecture major version (e.g., `8` for `sm_80`). |
| [Minor](#minor) | Gets the architecture minor version (e.g., `0` for `sm_80`, `6` for `sm_86`). |
| [ObjectName](#objectname) | Gets the optional object name or identifier assigned to this embedded module. |
| [Type](#type) | Gets the type of embedded file (`1` = PTX, `2` = ELF/cuBIN). |

## ComputeVersion

Gets the raw compute capability version (e.g., `80` for `sm_80`, `86` for `sm_86`).

```csharp
public uint ComputeVersion { get; }
```

## DataSize

Gets the size in bytes of the embedded module data payload.

```csharp
public ulong DataSize { get; }
```

## Major

Gets the architecture major version (e.g., `8` for `sm_80`).

```csharp
public ushort Major { get; }
```

## Minor

Gets the architecture minor version (e.g., `0` for `sm_80`, `6` for `sm_86`).

```csharp
public ushort Minor { get; }
```

## ObjectName

Gets the optional object name or identifier assigned to this embedded module.

```csharp
public string? ObjectName { get; }
```

## Type

Gets the type of embedded file (`1` = PTX, `2` = ELF/cuBIN).

```csharp
public ushort Type { get; }
```

## Examples

### Reading Embedded Module Metadata

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Fatbin;

// Execution Context: Host (CPU)
var item = new FatbinItemInfo(
    type: FatbinItemInfo.TypeCubin,
    major: 8,
    minor: 9,
    computeVersion: 89,
    objectName: "vector_add.sm_89.cubin",
    dataSize: 4096);

Console.WriteLine($"Module: {item.ObjectName}, IsCubin: {item.Type == FatbinItemInfo.TypeCubin}, Arch: sm_{item.Major}{item.Minor}");
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [FatbinImage Class](FatbinImage.md)
- [FatbinParser Class](FatbinParser.md)
