---
title: "GpuBigInteger.ModPow Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for multi-precision Modular Exponentiation (RSA) on the GPU."
---

## GpuBigIntDivRem.ModPow Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Computes the modular exponentiation $a^b \pmod m$ of extremely large integers. This is highly optimized for cryptography (e.g., RSA) using GPU-accelerated Montgomery reduction.

## Overloads

| Name                                                                                          | Description                                      |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| [ModPow(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)](#modpow) | Computes the modular exponentiation of a number. |

## GpuBigIntDivRem.ModPow(GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger, GpuUIntBigInteger)

Computes the modular exponentiation eagerly into the provided result buffer.

```csharp
public static void ModPow(GpuUIntBigInteger baseVal, GpuUIntBigInteger exp, GpuUIntBigInteger mod, GpuUIntBigInteger resultBuffer);
```

#### Parameters

`baseVal` _GpuUIntBigInteger_<br>
The base number.

`exp` _GpuUIntBigInteger_<br>
The exponent.

`mod` _GpuUIntBigInteger_<br>
The modulus.

`resultBuffer` _GpuUIntBigInteger_<br>
The buffer to hold the result.

#### Returns

_void_<br>
The operation writes its result to the `resultBuffer` parameter directly.

## Examples

### RSA Encryption / Decryption Orchestration

A concrete example demonstrating how `ModPow` is used to orchestrate RSA decryption ($Message = Ciphertext^d \pmod n$) across the GPU. This is highly effective when batch-processing thousands of payloads simultaneously.

```csharp
// Execution Context: Host (CPU)

// Allocate a context large enough for 2048-bit RSA keys
using var context = new GpuContext(maxWords: 64); // 64 words * 32 bits = 2048 bits

// 1. Prepare RSA Parameters (Modulus 'n', Private Exponent 'd')
byte[] modulusN = GetRsaModulus();
byte[] privateKeyD = GetRsaPrivateKey();
byte[] ciphertextBytes = GetIncomingCiphertext();

// 2. Upload to GPU VRAM
using var gpuModulus = GpuUIntBigInteger.FromByteArray(context, modulusN);
using var gpuPrivateKey = GpuUIntBigInteger.FromByteArray(context, privateKeyD);
using var gpuCiphertext = GpuUIntBigInteger.FromByteArray(context, ciphertextBytes);

// 3. Eagerly evaluate: c^d mod n
using var decryptedResult = new GpuUIntBigInteger(context, 64);
GpuBigIntDivRem.ModPow(gpuCiphertext, gpuPrivateKey, gpuModulus, decryptedResult);

// 5. Download the decrypted message back to the CPU
byte[] originalMessage = decryptedResult.ToByteArray();
Console.WriteLine($"Decrypted payload: {Convert.ToHexString(originalMessage)}");
```

## Remarks

This orchestration leverages Montgomery reduction to prevent massive allocation of intermediate memory during exponentiation.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [FermatRunner Class (G#)](Fermat.md)
