---
title: "FatbinParser Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides defensive parsing and inspection for NVIDIA FATBIN binaries (.fatbin, .gfatbin)."
---

## FatbinParser Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Fatbin`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Provides defensive parsing and inspection for NVIDIA FATBIN binaries (`.fatbin`, `.gfatbin`).

```csharp
public static class FatbinParser
```

## Fields

| Name | Description |
| --- | --- |
| [GSharpFatbinExtension](#gsharpfatbinextension) | Recommended file extension for G#-signed fatbins (`".gfatbin"`). |
| [GSharpSignatureSize](#gsharpsignaturesize) | Size of the G# appended Ed25519 digital signature in bytes (`64`). |

## GSharpFatbinExtension

Recommended file extension for G#-signed fatbins.

```csharp
public const string GSharpFatbinExtension = ".gfatbin";
```

## GSharpSignatureSize

Size of the G# appended Ed25519 digital signature in bytes.

```csharp
public const int GSharpSignatureSize = 64;
```

## Methods

| Name | Description |
| --- | --- |
| [TryParse(byte[], out FatbinImage)](#tryparsebyte-out-fatbinimage) | Attempts to defensively parse a fatbin binary byte array. |
| [TryParse(ReadOnlyMemory\<byte\>, out FatbinImage)](#tryparsereadonlymemorybyte-out-fatbinimage) | Attempts to defensively parse a fatbin binary memory buffer. |
| [TryParse(ReadOnlySpan\<byte\>, out FatbinImage)](#tryparsereadonlyspanbyte-out-fatbinimage) | Attempts to defensively parse a fatbin binary span. |

## TryParse(byte[], out FatbinImage)

Attempts to defensively parse a fatbin binary byte array. Returns `false` gracefully if headers do not match expected magic or bounds without throwing exceptions.

```csharp
public static bool TryParse(
    byte[]? binary,
    [NotNullWhen(true)] out FatbinImage? fatbin);
```

#### Parameters

`binary` _Byte[]_<br>
The byte array containing the fatbin binary data.

`fatbin` _[FatbinImage](FatbinImage.md)_<br>
When this method returns, contains the parsed `FatbinImage` if successful; otherwise, `null`.

#### Returns

_Boolean_<br>
`true` if parsing succeeded; otherwise, `false`.

## TryParse(ReadOnlyMemory\<byte\>, out FatbinImage)

Attempts to defensively parse a fatbin binary memory buffer. Returns `false` gracefully if headers do not match expected magic or bounds without throwing exceptions.

```csharp
public static bool TryParse(
    ReadOnlyMemory<byte> binary,
    [NotNullWhen(true)] out FatbinImage? fatbin);
```

#### Parameters

`binary` _ReadOnlyMemory\<Byte\>_<br>
The memory buffer containing the fatbin binary data.

`fatbin` _[FatbinImage](FatbinImage.md)_<br>
When this method returns, contains the parsed `FatbinImage` if successful; otherwise, `null`.

#### Returns

_Boolean_<br>
`true` if parsing succeeded; otherwise, `false`.

## TryParse(ReadOnlySpan\<byte\>, out FatbinImage)

Attempts to defensively parse a fatbin binary span. Returns `false` gracefully if headers do not match expected magic or bounds without throwing exceptions.

```csharp
public static bool TryParse(
    ReadOnlySpan<byte> binary,
    [NotNullWhen(true)] out FatbinImage? fatbin);
```

#### Parameters

`binary` _ReadOnlySpan\<Byte\>_<br>
The byte span containing the fatbin binary data.

`fatbin` _[FatbinImage](FatbinImage.md)_<br>
When this method returns, contains the parsed `FatbinImage` if successful; otherwise, `null`.

#### Returns

_Boolean_<br>
`true` if parsing succeeded; otherwise, `false`.

## Remarks

`FatbinParser` validates the standard NVIDIA FATBIN ELF magic number (`0xBA55ED50`) and header bounds before enumerating embedded PTX and cuBIN items. If the total buffer length equals the declared FATBIN payload size plus `GSharpSignatureSize` (`64` bytes), `HasGSharpSignature` is set to `true` on the resulting [FatbinImage](FatbinImage.md) and the trailing 64-byte slice is exposed via `FatbinImage.Signature`.

When parsing a `ReadOnlyMemory<byte>` or `byte[]`, the resulting `FatbinImage.Payload` and `FatbinImage.Signature` slice the backing memory directly without copying. When parsing a `ReadOnlySpan<byte>`, the payload and signature slices are copied into managed arrays.

## Examples

### Inspecting a FATBIN Payload

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using System.Buffers.Binary;
using Kassyi.GSharp.Runtime.Fatbin;

// Execution Context: Host (CPU)
// Construct a minimal synthetic 56-byte FATBIN buffer containing a single sm_80 PTX item header
byte[] buffer = new byte[56];
BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), 0xBA55ED50u); // Magic
BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), 1);           // Version
BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), 16);          // HeaderSize
BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8, 8), 40UL);        // PayloadItemsSize

// Item header at offset 16 (40 bytes)
BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(16 + 0x00, 2), FatbinItemInfo.TypePtx);
BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16 + 0x04, 4), 40u); // ItemHeaderSize
BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16 + 0x08, 8), 0UL); // ItemDataSize
BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16 + 0x1C, 4), 80u); // ComputeVersion (sm_80)

if (FatbinParser.TryParse(buffer, out var image))
{
    Console.WriteLine($"Magic: 0x{image.Magic:X8}, Items: {image.Items.Count}");
    FatbinItemInfo item = image.Items[0];
    Console.WriteLine($"Item[0] Type: {item.Type}, Arch: sm_{item.Major}{item.Minor}, Compatible with sm_86: {image.IsCompatibleWith(8, 6)}");
}
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [FatbinImage Class](FatbinImage.md)
- [FatbinItemInfo Struct](FatbinItemInfo.md)
- [GpuKernelLoader Class](GpuKernelLoader.md)
