---
title: "FatbinImage Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Represents a parsed NVIDIA FATBIN binary image and its embedded targets."
---

## FatbinImage Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Fatbin`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Represents a parsed NVIDIA FATBIN binary image and its embedded targets.

```csharp
public sealed class FatbinImage
```

## Constructors

| Name | Description |
| --- | --- |
| [FatbinImage(UInt32, UInt16, UInt64, Boolean, ReadOnlyMemory\<Byte\>, ReadOnlyMemory\<Byte\>, IReadOnlyList\<FatbinItemInfo\>)](#fatbinimageuint32-uint16-uint64-boolean-readonlymemorybyte-readonlymemorybyte-ireadonlylistfatbiniteminfo) | Initializes a new instance of the `FatbinImage` class. |

## FatbinImage(UInt32, UInt16, UInt64, Boolean, ReadOnlyMemory\<Byte\>, ReadOnlyMemory\<Byte\>, IReadOnlyList\<FatbinItemInfo\>)

Initializes a new instance of the `FatbinImage` class.

```csharp
public FatbinImage(
    uint magic,
    ushort version,
    ulong payloadSize,
    bool hasGSharpSignature,
    ReadOnlyMemory<byte> payload,
    ReadOnlyMemory<byte> signature,
    IReadOnlyList<FatbinItemInfo> items);
```

#### Parameters

`magic` _UInt32_<br>
The FATBIN magic number.

`version` _UInt16_<br>
The FATBIN format version.

`payloadSize` _UInt64_<br>
The total payload size in bytes.

`hasGSharpSignature` _Boolean_<br>
Whether a G# 64-byte Ed25519 signature is present.

`payload` _ReadOnlyMemory\<Byte\>_<br>
The binary payload data slice.

`signature` _ReadOnlyMemory\<Byte\>_<br>
The appended signature slice, if present.

`items` _IReadOnlyList\<[FatbinItemInfo](FatbinItemInfo.md)\>_<br>
The collection of parsed embedded modules.

## Properties

| Name | Description |
| --- | --- |
| [HasGSharpSignature](#hasgsharpsignature) | Gets a value indicating whether a G# 64-byte Ed25519 digital signature is detected at the tail. |
| [Items](#items) | Gets the metadata of all embedded modules (PTX or cuBIN) contained in this fatbin. |
| [Magic](#magic) | Gets the FATBIN magic number (expected to be `0xBA55ED50`). |
| [Payload](#payload) | Gets the raw FATBIN binary payload (excluding signature). |
| [PayloadSize](#payloadsize) | Gets the total size of the FATBIN binary payload in bytes (including headers, excluding appended signatures). |
| [Signature](#signature) | Gets the appended 64-byte G# Ed25519 digital signature, or an empty memory slice if not signed. |
| [Version](#version) | Gets the FATBIN format version. |

## HasGSharpSignature

Gets a value indicating whether a G# 64-byte Ed25519 digital signature is detected at the tail. Determined strictly by whether the source binary length equals `PayloadSize + 64`.

```csharp
public bool HasGSharpSignature { get; }
```

## Items

Gets the metadata of all embedded modules (PTX or cuBIN) contained in this fatbin.

```csharp
public IReadOnlyList<FatbinItemInfo> Items { get; }
```

## Magic

Gets the FATBIN magic number (expected to be `0xBA55ED50`).

```csharp
public uint Magic { get; }
```

## Payload

Gets the raw FATBIN binary payload (excluding signature).

```csharp
public ReadOnlyMemory<byte> Payload { get; }
```

## PayloadSize

Gets the total size of the FATBIN binary payload in bytes (including headers, excluding appended signatures).

```csharp
public ulong PayloadSize { get; }
```

## Signature

Gets the appended 64-byte G# Ed25519 digital signature, or an empty memory slice if not signed.

```csharp
public ReadOnlyMemory<byte> Signature { get; }
```

## Version

Gets the FATBIN format version.

```csharp
public ushort Version { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [IsCompatibleWith(Int32, Int32)](#iscompatiblewithint32-int32) | Verifies if any embedded module within this FATBIN image is compatible with the specified GPU compute capability. |
| [VerifySignature(ReadOnlySpan\<Byte\>)](#verifysignaturereadonlyspanbyte) | Verifies the appended Ed25519 signature against one or more trusted 32-byte public keys (Authorized Keys model). |

## IsCompatibleWith(Int32, Int32)

Verifies if any embedded module within this FATBIN image is compatible with the specified GPU compute capability.

```csharp
public bool IsCompatibleWith(int deviceMajor, int deviceMinor);
```

#### Parameters

`deviceMajor` _Int32_<br>
The major compute capability version of the physical GPU (e.g., `8` for `sm_80`).

`deviceMinor` _Int32_<br>
The minor compute capability version of the physical GPU (e.g., `0` for `sm_80`, `6` for `sm_86`).

#### Returns

_Boolean_<br>
`true` if at least one module can execute on the target architecture; otherwise, `false`.

## VerifySignature(ReadOnlySpan\<Byte\>)

Verifies the appended Ed25519 signature against one or more trusted 32-byte public keys (Authorized Keys model).

```csharp
public bool VerifySignature(ReadOnlySpan<byte> trustedKeys);
```

#### Parameters

`trustedKeys` _ReadOnlySpan\<Byte\>_<br>
A contiguous byte span containing one or more 32-byte public keys.

#### Returns

_Boolean_<br>
`true` if the signature matches any trusted key in the whitelist; otherwise, `false`.

## Remarks

`IsCompatibleWith` evaluates architecture compatibility according to NVIDIA module semantics:
- **PTX (`FatbinItemInfo.TypePtx`)**: Supports forward-compatible JIT compilation and returns `true` when the target GPU architecture is equal to or newer than the embedded PTX target (`deviceMajor > item.Major || (deviceMajor == item.Major && deviceMinor >= item.Minor)`).
- **cuBIN (`FatbinItemInfo.TypeCubin`)**: Binary compatible within one major version, from its own minor version up (`item.Major == deviceMajor && deviceMinor >= item.Minor`): an `sm_86` cuBIN runs on 8.6 and 8.9, but not on 8.0 or 9.0.
- The check accepts everything the driver may accept. Architecture-specific (`sm_90a`) and family-specific (`sm_100f`) cuBINs, and Tegra GPUs (8.7, Jetson Orin), which are not binary compatible with the desktop GPUs of the same major version, follow stricter or wider rules that it does not model; the driver makes the final decision, and `GpuKernelLoader` reports the driver's `CUDA_ERROR_NO_BINARY_FOR_GPU` with the same message as the pre-check.

## Examples

### Checking Architecture Compatibility

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using System.Buffers.Binary;
using Kassyi.GSharp.Runtime.Fatbin;

// Execution Context: Host (CPU)
byte[] buffer = new byte[56];
BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0, 4), 0xBA55ED50u);
BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(4, 2), 1);
BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(6, 2), 16);
BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8, 8), 40UL);

BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(16 + 0x00, 2), FatbinItemInfo.TypePtx);
BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16 + 0x04, 4), 40u);
BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(16 + 0x08, 8), 0UL);
BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(16 + 0x1C, 4), 80u);

if (FatbinParser.TryParse(buffer, out var image))
{
    Console.WriteLine($"PayloadSize: {image.PayloadSize}, Signed: {image.HasGSharpSignature}");
    Console.WriteLine($"Compatible with sm_75: {image.IsCompatibleWith(7, 5)}");
    Console.WriteLine($"Compatible with sm_86: {image.IsCompatibleWith(8, 6)}");
}
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [FatbinParser Class](FatbinParser.md)
- [FatbinItemInfo Struct](FatbinItemInfo.md)
