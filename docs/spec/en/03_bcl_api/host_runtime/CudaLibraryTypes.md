---
title: "CudaLibraryTypes Enums (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Status and operation enumerations for NVIDIA cuBLAS and cuRAND native library interop."
---

## CudaLibraryTypes Enums

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Interop`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Provides status codes, matrix transpose operation selectors, and random number generator algorithm enumerations for NVIDIA cuBLAS and cuRAND native library interop.

```csharp
public enum CuBlasStatus
public enum CuBlasOperation
public enum CuRandStatus
public enum CuRandRngType
```

## Types

| Name | Description |
| --- | --- |
| [CuBlasStatus](#cublasstatus-enum) | Represents `cublasStatus_t` return codes of the cuBLAS library. |
| [CuBlasOperation](#cublasoperation-enum) | Represents `cublasOperation_t`, the operation applied to a matrix operand. |
| [CuRandStatus](#curandstatus-enum) | Represents `curandStatus_t` return codes of the cuRAND library. |
| [CuRandRngType](#curandrngtype-enum) | Represents `curandRngType_t`, the algorithm used by a cuRAND generator. |

## CuBlasStatus Enum

Represents `cublasStatus_t` return codes of the cuBLAS library.

```csharp
public enum CuBlasStatus
{
    Success = 0,
    NotInitialized = 1,
    AllocFailed = 3,
    InvalidValue = 7,
    ArchMismatch = 8,
    MappingError = 11,
    ExecutionFailed = 13,
    InternalError = 14,
    NotSupported = 15,
    LicenseError = 16
}
```

## CuBlasOperation Enum

Represents `cublasOperation_t`, the operation applied to a matrix operand.

```csharp
public enum CuBlasOperation
{
    None = 0,
    Transpose = 1,
    ConjugateTranspose = 2
}
```

## CuRandStatus Enum

Represents `curandStatus_t` return codes of the cuRAND library.

```csharp
public enum CuRandStatus
{
    Success = 0,
    VersionMismatch = 100,
    NotInitialized = 101,
    AllocationFailed = 102,
    TypeError = 103,
    OutOfRange = 104,
    LengthNotMultiple = 105,
    DoublePrecisionRequired = 106,
    LaunchFailure = 201,
    PreexistingFailure = 202,
    InitializationFailed = 203,
    ArchMismatch = 204,
    InternalError = 999
}
```

## CuRandRngType Enum

Represents `curandRngType_t`, the algorithm used by a cuRAND generator.

```csharp
public enum CuRandRngType
{
    PseudoDefault = 100,
    PseudoXorwow = 101,
    PseudoMrg32k3a = 121,
    PseudoMtgp32 = 141,
    PseudoMt19937 = 142,
    PseudoPhilox4x32x10 = 161,
    QuasiDefault = 200,
    QuasiSobol32 = 201,
    QuasiScrambledSobol32 = 202,
    QuasiSobol64 = 203,
    QuasiScrambledSobol64 = 204
}
```

## Examples

### Selecting cuBLAS and cuRAND Options

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Interop;

// Execution Context: Host (CPU)
Console.WriteLine($"cuBLAS Op: {CuBlasOperation.Transpose}, cuRAND RNG: {CuRandRngType.PseudoPhilox4x32x10}");
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CudaDriverApi and Driver Interop Types](CudaDriverApi.md)
