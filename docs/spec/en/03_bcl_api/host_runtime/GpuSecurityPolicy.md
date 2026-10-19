---
title: "GpuSecurityPolicy Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Manages trusted Ed25519 public keys and signature verification policies for GPU binaries."
---

## GpuSecurityPolicy Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Fatbin`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Manages trusted Ed25519 public keys and signature verification policies for GPU binaries.

```csharp
public static partial class GpuSecurityPolicy
```

## Properties

| Name | Description |
| --- | --- |
| [EmbeddedKeys](#embeddedkeys) | Gets the embedded trusted Ed25519 public keys (concatenated 32-byte keys). |

## EmbeddedKeys

Gets the embedded trusted Ed25519 public keys (concatenated 32-byte keys).

```csharp
public static ReadOnlySpan<byte> EmbeddedKeys { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [RegisterTrustedKey(ReadOnlySpan\<Byte\>)](#registertrustedkeyreadonlyspanbyte) | Registers a trusted 32-byte Ed25519 public key dynamically at runtime. |
| [VerifyImage(FatbinImage)](#verifyimagefatbinimage) | Verifies whether the specified FATBIN image has a valid digital signature matching any embedded or dynamic trusted key. |

## RegisterTrustedKey(ReadOnlySpan\<Byte\>)

Registers a trusted 32-byte Ed25519 public key dynamically at runtime.

```csharp
public static void RegisterTrustedKey(ReadOnlySpan<byte> publicKey);
```

#### Parameters

`publicKey` _ReadOnlySpan\<Byte\>_<br>
The 32-byte public key.

#### Exceptions

`ArgumentException`<br>
`publicKey` is not exactly 32 bytes long, is not a canonical and decompressible Ed25519 point (RFC 8032 Section 5.1.3), or is one of the 8 small-order points (including the identity). Small-order keys are rejected because some verifiers accept forged signatures for them.

## VerifyImage(FatbinImage)

Verifies whether the specified FATBIN image has a valid digital signature matching any embedded or dynamic trusted key.

```csharp
public static bool VerifyImage(FatbinImage image);
```

#### Parameters

`image` _[FatbinImage](FatbinImage.md)_<br>
The parsed FATBIN image to verify.

#### Returns

_Boolean_<br>
`true` if the signature matches an authorized trusted key; otherwise, `false`.

#### Exceptions

`ArgumentNullException`<br>
`image` is `null`.

## Remarks

`GpuSecurityPolicy` implements an Authorized Keys model for Ed25519 signature verification of `.gfatbin` payloads. By default, `EmbeddedKeys` returns the compile-time embedded public keys (or the development fallback key when custom keys are not injected via partial method `GetCustomEmbeddedKeys`). Additional keys can be appended in a thread-safe manner at runtime using `RegisterTrustedKey`.

## Examples

### Inspecting and Registering Trusted Public Keys

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Fatbin;

// Execution Context: Host (CPU)
Console.WriteLine($"Embedded keys length: {GpuSecurityPolicy.EmbeddedKeys.Length}");

// RFC 8032 Section 7.1 Test Vector 2 public key. All-zero or other small-order keys are rejected.
byte[] customPublicKey = Convert.FromHexString("3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c");
GpuSecurityPolicy.RegisterTrustedKey(customPublicKey);
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [FatbinImage Class](FatbinImage.md)
- [FatbinParser Class](FatbinParser.md)
- [GpuKernelLoader Class](GpuKernelLoader.md)
