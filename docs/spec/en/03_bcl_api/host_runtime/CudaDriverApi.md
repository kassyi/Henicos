---
title: "CudaDriverApi and Driver Interop Types (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "P/Invoke bindings, status enums, exception types, and descriptors for the NVIDIA CUDA Driver API."
---

## CudaDriverApi and Driver Interop Types

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Interop`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Provides P/Invoke bindings, status enumerations, `CudaException`, and resource descriptor structures for the NVIDIA CUDA Driver API (`nvcuda.dll` / `libcuda.so`).

```csharp
public static partial class CudaDriverApi
public class CudaException : Exception
public enum CUresult
public enum CuFunctionAttribute
public enum CUstreamCaptureStatus
public struct CudaArrayDescriptor
public struct CudaResourceDescriptor
public struct CudaTextureDescriptor
public struct CudaResourceViewDescriptor
```

## Types

| Name | Description |
| --- | --- |
| [CudaDriverApi](#cudadriverapi-class) | P/Invoke bindings and device attribute constants for the NVIDIA CUDA Driver API. |
| [CudaException](#cudaexception-class) | Exception thrown when a CUDA Driver API call returns a non-success status code. |
| [CUresult](#curesult-enum) | Represents CUDA Driver API error return codes. |
| [CuFunctionAttribute](#cufunctionattribute-enum) | Represents function attributes that can be queried from a `CUfunction` handle. |
| [CUstreamCaptureStatus](#custreamcapturestatus-enum) | Represents the capture status of a CUDA stream. |
| [CudaArrayDescriptor](#cudaarraydescriptor-struct) | Represents a 1D or 2D CUDA array descriptor. |
| [CudaResourceDescriptor](#cudaresourcedescriptor-struct) | Represents a CUDA resource descriptor. |
| [CudaTextureDescriptor](#cudatexturedescriptor-struct) | Represents a CUDA texture descriptor. |
| [CudaResourceViewDescriptor](#cudaresourceviewdescriptor-struct) | Represents a CUDA resource view descriptor. |

## CudaDriverApi Class

P/Invoke bindings for NVIDIA CUDA Driver API functions (`nvcuda.dll` / `libcuda.so`). All `[LibraryImport]` P/Invoke methods on `CudaDriverApi` are generated at compile time from `src/NativeApi/cuda_driver_api.yml`.

```csharp
public static partial class CudaDriverApi
{
    public enum CuDeviceAttribute
    {
        MultiprocessorCount = 16,
        ComputeCapabilityMajor = 75,
        ComputeCapabilityMinor = 76
    }
}
```

## CudaException Class

Exception thrown when a CUDA Driver API call returns a non-success status code.

```csharp
public class CudaException(CUresult result, string? message = null, Exception? innerException = null) : Exception
{
    public CUresult Result { get; }
    public static void ThrowIfError(CUresult result);
}
```

## CUresult Enum

Represents CUDA Driver API error return codes.

```csharp
public enum CUresult
{
    CudaSuccess = 0,
    CudaErrorInvalidValue = 1,
    CudaErrorOutOfMemory = 2,
    CudaErrorNotInitialized = 3,
    CudaErrorDeinitialized = 4,
    CudaErrorNoDevice = 100,
    CudaErrorInvalidDevice = 101,
    CudaErrorInvalidImage = 200,
    CudaErrorInvalidContext = 201,
    CudaErrorContextAlreadyCurrent = 202,
    CudaErrorMapFailed = 205,
    CudaErrorUnmapFailed = 206,
    CudaErrorArrayIsMapped = 207,
    CudaErrorAlreadyMapped = 208,
    CudaErrorNoBinaryForGpu = 209,
    CudaErrorAlreadyAcquired = 210,
    CudaErrorNotMapped = 211,
    CudaErrorNotMappedAsArray = 212,
    CudaErrorNotMappedAsPointer = 213,
    CudaErrorEccUncorrectable = 214,
    CudaErrorUnsupportedLimit = 215,
    CudaErrorContextAlreadyInUse = 216,
    CudaErrorUnsupportedPtxVersion = 222,
    CudaErrorInvalidSource = 300,
    CudaErrorFileNotFound = 301,
    CudaErrorSharedObjectSymbolNotFound = 302,
    CudaErrorSharedObjectInitFailed = 303,
    CudaErrorOperatingSystem = 304,
    CudaErrorInvalidHandle = 400,
    CudaErrorNotFound = 500,
    CudaErrorNotReady = 600,
    CudaErrorIllegalAddress = 700,
    CudaErrorLaunchOutOfResources = 701,
    CudaErrorLaunchTimeout = 702,
    CudaErrorLaunchIncompatibleTexturing = 703,
    CudaErrorAssert = 710,
    CudaErrorIllegalInstruction = 715,
    CudaErrorLaunchFailed = 719,
    CudaErrorNotSupported = 801,
    CudaErrorStreamCaptureUnsupported = 900,
    CudaErrorStreamCaptureInvalidated = 901,
    CudaErrorStreamCaptureMerge = 902,
    CudaErrorStreamCaptureUnmatched = 903,
    CudaErrorStreamCaptureUnjoined = 904,
    CudaErrorStreamCaptureIsolation = 905,
    CudaErrorStreamCaptureImplicit = 906,
    CudaErrorCapturedEvent = 907,
    CudaErrorUnknown = 999
}
```

## CuFunctionAttribute Enum

Represents function attributes that can be queried from a `CUfunction` handle.

```csharp
public enum CuFunctionAttribute
{
    MaxThreadsPerBlock = 0,
    SharedSizeBytes = 1,
    ConstSizeBytes = 2,
    LocalSizeBytes = 3,
    NumRegs = 4,
    PtxVersion = 5,
    BinaryVersion = 6
}
```

## CUstreamCaptureStatus Enum

Represents the capture status of a CUDA stream.

```csharp
public enum CUstreamCaptureStatus
{
    None = 0,
    Active = 1,
    Invalidated = 2
}
```

## CudaArrayDescriptor Struct

Represents a 1D or 2D CUDA array descriptor.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CudaArrayDescriptor
{
    public nuint Width;
    public nuint Height;
    public int Format;
    public uint NumChannels;
}
```

## CudaResourceDescriptor Struct

Represents a CUDA resource descriptor.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CudaResourceDescriptor
{
    public int ResType;
    public IntPtr Res;
    public uint Flags;
}
```

## CudaTextureDescriptor Struct

Represents a CUDA texture descriptor.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CudaTextureDescriptor
{
    public int AddressMode0;
    public int AddressMode1;
    public int AddressMode2;
    public int FilterMode;
    public uint Flags;
    public uint MaxAnisotropy;
    public int MipmapFilterMode;
    public float MipmapLevelBias;
    public float MinMipmapLevelClamp;
    public float MaxMipmapLevelClamp;
    public float BorderColor0;
    public float BorderColor1;
    public float BorderColor2;
    public float BorderColor3;
}
```

## CudaResourceViewDescriptor Struct

Represents a CUDA resource view descriptor.

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CudaResourceViewDescriptor
{
    public int Format;
    public nuint Width;
    public nuint Height;
    public nuint Depth;
    public uint FirstMipmapLevel;
    public uint LastMipmapLevel;
    public uint FirstLayer;
    public uint LastLayer;
    public uint Flags;
}
```

## Examples

### Checking CUDA Driver Status Codes

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Interop;

// Execution Context: Host (CPU)
CudaException.ThrowIfError(CUresult.CudaSuccess);
Console.WriteLine($"SM Attribute ID: {(int)CudaDriverApi.CuDeviceAttribute.MultiprocessorCount}");
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CudaLibraryTypes](CudaLibraryTypes.md)
- [7. Diagnostics and Exceptions](../01_diagnostics_and_exceptions.md)
