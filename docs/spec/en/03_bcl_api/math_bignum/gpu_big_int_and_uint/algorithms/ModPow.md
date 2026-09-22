---
title: "GpuBigInteger.ModPow Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Reference for multi-precision Modular Exponentiation (RSA) on the GPU."
---

## GpuBigInteger.ModPow Method

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Computes the modular exponentiation $a^b \pmod m$ of extremely large integers. This is highly optimized for cryptography (e.g., RSA) using GPU-accelerated Montgomery reduction.

## Overloads

| Name | Description |
| --- | --- |
| [ModPow(GpuBigInteger, GpuBigInteger, GpuBigInteger)](#modpow-gpubiginteger-gpubiginteger-gpubiginteger) | Computes the modular exponentiation of a number. |

## ModPow(GpuBigInteger, GpuBigInteger, GpuBigInteger)

Builds a lazy-evaluated expression tree node for the ModPow operation.

```csharp
public static GpuBigIntExpr ModPow(GpuBigInteger value, GpuBigInteger exponent, GpuBigInteger modulus);
```

#### Parameters

`value` _GpuBigInteger_<br>
The base number.

`exponent` _GpuBigInteger_<br>
The exponent.

`modulus` _GpuBigInteger_<br>
The modulus.

#### Returns

_GpuBigIntExpr_<br>
An expression tree node that, when evaluated, produces the result of $value^{exponent} \pmod{modulus}$.

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

var n = new GpuBigInteger(gpuModulus);
var d = new GpuBigInteger(gpuPrivateKey);
var c = new GpuBigInteger(gpuCiphertext);

// 3. Build the Lazy Expression for Decryption: c^d mod n
var decryptExpr = GpuBigInteger.ModPow(c, d, n);

// 4. Dispatch kernels and Evaluate
using var decryptedResult = decryptExpr.Node.Evaluate(context, null);

// 5. Download the decrypted message back to the CPU
byte[] originalMessage = decryptedResult.ToByteArray();
Console.WriteLine($"Decrypted payload: {Convert.ToHexString(originalMessage)}");
```

## Remarks

This orchestration leverages Montgomery reduction to prevent massive allocation of intermediate memory during exponentiation.

## See also

### Related Algorithms
- [Fermat](Fermat.md)
- [DivRem](DivRem.md)
- [Gcd](Gcd.md)
- [Ntt](Ntt.md)

### Integer Types & Architecture
- [GpuBigInteger](../GpuBigInteger.md)
- [GpuUIntBigInteger](../GpuUIntBigInteger.md)
- [GpuBigInteger and GpuUIntBigInteger Overview](../00_overview.md)
- [GpuContext](../../gpu_context/GpuContext.md)
- [Arbitrary-Precision Math Overview](../../00_overview.md)
- [Gpu BCL API Overview](../../../00_overview.md)

### Guides
- [Stdlib Math and Lazy Evaluation Guide](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [Precision and Performance Guide](../../../../04_guides/05_precision_and_performance_guide.md)
