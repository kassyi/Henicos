using System.Runtime.CompilerServices;

[assembly:
    SuppressMessage("ReSharper", "UnusedMember.Global", Scope = "namespaceanddescendants",
        Target = "Kassyi.GSharp.Runtime.Interop", Justification = "Public API exposed for runtime consumers")]

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Represents CUDA Driver API error return codes.</summary>
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
    CudaErrorInvalidSource = 300,
    CudaErrorFileNotFound = 301,
    CudaErrorSharedObjectSymbolNotFound = 302,
    CudaErrorSharedObjectInitFailed = 303,
    CudaErrorOperatingSystem = 304,
    CudaErrorInvalidHandle = 400,
    CudaErrorNotFound = 500,
    CudaErrorNotReady = 600,
    CudaErrorLaunchFailed = 700,
    CudaErrorLaunchOutOfResources = 701,
    CudaErrorLaunchTimeout = 702,
    CudaErrorLaunchIncompatibleTexturing = 703,
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

/// <summary>Represents function attributes that can be queried from a CUfunction handle.</summary>
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

/// <summary>Represents the capture status of a CUDA stream.</summary>
public enum CUstreamCaptureStatus
{
    None = 0,
    Active = 1,
    Invalidated = 2
}

/// <summary>Exception thrown when a CUDA Driver API call returns a non-success status code.</summary>
public class CudaException(CUresult result, string? message = null) : Exception(message ?? $"CUDA Error: {result}")
{
    public CUresult Result { get; } = result;

    public static void ThrowIfError(CUresult result)
    {
        if (result != CUresult.CudaSuccess)
        {
            throw new CudaException(result);
        }
    }
}

/// <summary>P/Invoke bindings for NVIDIA CUDA Driver API functions (<c>nvcuda.dll</c> / <c>libcuda.so</c>).</summary>
// ReSharper disable once InconsistentNaming -- P/Invoke matches native CUDA API naming
public static partial class CudaDriverApi
{
    // NOTE FOR AI AGENTS & DEVELOPERS:
    // DO NOT declare [LibraryImport] P/Invoke methods manually in this file.
    // All CUDA Driver API bindings are automatically generated from 'src/NativeApi/cuda_driver_api.yml' at compile time.
    // To add or modify Driver API methods, declare them in the YAML file instead.

    private const string DllName = "nvcuda.dll";

    public enum CuDeviceAttribute
    {
        MultiprocessorCount = 16,
        ComputeCapabilityMajor = 75,
        ComputeCapabilityMinor = 76
    }
}

