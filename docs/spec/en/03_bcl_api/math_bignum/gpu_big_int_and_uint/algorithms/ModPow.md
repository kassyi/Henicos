---
title: "GpuBigInt.ModPow Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for multi-precision Modular Exponentiation (RSA) on the GPU."
---

## GpuBigIntDivRem.ModPow Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Computes the modular exponentiation $a^b \pmod m$ of extremely large integers on the GPU.

> [!IMPORTANT]
> **Poly-Algorithm Dispatcher & Zero-Sync Fallback**
> ModPow uses a poly-algorithm architecture governed by `GpuWorkspace.PolyAlgorithmThreshold` (default: 1024 bits).
> - **MegaKernel (NTT-Barrett)**: Eagerly evaluated within GPU hardware registers. Provides extreme speedup for operands $\le 1024$ bits.
> - **Zero-Sync Controller**: For giant-scale operands $> 1024$ bits where MegaKernels suffer catastrophic register spilling, ModPow safely falls back to a Zero-Sync model. This mode dispatches thousands of operations without CPU `cuStreamSynchronize` overhead, allowing `ModPow` to be safely captured entirely within CUDA Graphs.

## Overloads

| Name                                                                                          | Description                                      |
| --------------------------------------------------------------------------------------------- | ------------------------------------------------ |
| [ModPow(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modpow) | Computes the modular exponentiation of a number. |

## GpuBigIntDivRem.ModPow(GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the modular exponentiation eagerly into the provided result buffer.

```csharp
public static void ModPow(GpuBigUInt baseVal, GpuBigUInt exp, GpuBigUInt mod, GpuBigUInt resultBuffer);
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

> [!NOTE] 
> Requires assemblies: `Kassyi.GSharp.Stdlib.GpuMath`

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

class Program
{
    static void Main()
    {
        // Allocate a workspace large enough for 2048-bit RSA keys
        using var workspace = new GpuWorkspace(maxWords: 64); // 64 words * 32 bits = 2048 bits

        // 1. Prepare RSA Parameters (Modulus 'n', Private Exponent 'd')
        byte[] modulusN = GetRsaModulus();
        byte[] privateKeyD = GetRsaPrivateKey();
        byte[] ciphertextBytes = GetIncomingCiphertext();

        // 2. Upload to GPU VRAM
        using var gpuModulus = GpuBigUInt.FromBigInteger(workspace, new BigInteger(modulusN, isUnsigned: true, isBigEndian: true), 64);
        using var gpuPrivateKey = GpuBigUInt.FromBigInteger(workspace, new BigInteger(privateKeyD, isUnsigned: true, isBigEndian: true), 64);
        using var gpuCiphertext = GpuBigUInt.FromBigInteger(workspace, new BigInteger(ciphertextBytes, isUnsigned: true, isBigEndian: true), 64);

        // 3. Eagerly evaluate: c^d mod n
        using var decryptedResult = new GpuBigUInt(workspace, 64);
        GpuBigIntDivRem.ModPow(gpuCiphertext, gpuPrivateKey, gpuModulus, decryptedResult);

        // 5. Download the decrypted message back to the CPU
        byte[] originalMessage = decryptedResult.ToBigInteger().ToByteArray(isUnsigned: true, isBigEndian: true);
        Console.WriteLine($"Decrypted payload: {Convert.ToHexString(originalMessage)}");
    }

    static byte[] GetRsaModulus() { var b = new byte[256]; b[255] = 1; return b; }
    static byte[] GetRsaPrivateKey() { var b = new byte[256]; b[255] = 3; return b; }
    static byte[] GetIncomingCiphertext() { var b = new byte[256]; b[255] = 42; return b; }
}
```

## Remarks

This orchestration leverages Montgomery reduction to prevent massive allocation of intermediate memory during exponentiation.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../../00_index.md)
- [FermatAlgorithm Class (G#)](FermatAlgorithm.md)
