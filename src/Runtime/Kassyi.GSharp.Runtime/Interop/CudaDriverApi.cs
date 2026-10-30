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
    CudaErrorUnsupportedPtxVersion = 222,
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

/// <summary>Represents function attributes that can be queried from a CUfunction handle.</summary>
public enum CuFunctionAttribute
{
    MaxThreadsPerBlock = 0,
    SharedSizeBytes = 1,
    ConstSizeBytes = 2,
    LocalSizeBytes = 3,
    NumRegs = 4,
    PtxVersion = 5,
    BinaryVersion = 6,

    /// <summary>
    /// The largest dynamic shared memory in bytes that a launch of the function may request. It starts at the default
    /// per-block limit minus the static shared memory and can be raised with <c>cuFuncSetAttribute</c> up to the opt-in
    /// limit of the device minus the static shared memory.
    /// </summary>
    MaxDynamicSharedSizeBytes = 8
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
        /// <summary>The shared memory per block in bytes that every launch may use without an opt-in (48 KB).</summary>
        MaxSharedMemoryPerBlock = 8,
        MultiprocessorCount = 16,
        ComputeCapabilityMajor = 75,
        ComputeCapabilityMinor = 76,
        /// <summary>The shared memory per multiprocessor in bytes.</summary>
        MaxSharedMemoryPerMultiprocessor = 81,
        /// <summary>The shared memory per block in bytes that a function may use after it opts in (static plus dynamic).</summary>
        MaxSharedMemoryPerBlockOptin = 97,
        /// <summary>The shared memory in bytes that the system reserves for each block on top of the user's.</summary>
        ReservedSharedMemoryPerBlock = 111,
        CanUse64BitStreamMemOps = 122,
        CanUseStreamWaitValueNor = 123
    }
}

/// <summary>Represents a 1D or 2D CUDA array descriptor.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CudaArrayDescriptor
{
    /// <summary>Width of the array.</summary>
    public nuint Width;

    /// <summary>Height of the array.</summary>
    public nuint Height;

    /// <summary>Array format code.</summary>
    public int Format;

    /// <summary>Number of channels per element.</summary>
    public uint NumChannels;
}

/// <summary>Represents a CUDA resource descriptor.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CudaResourceDescriptor
{
    /// <summary>Resource type.</summary>
    public int ResType;

    /// <summary>Resource handle or pointer.</summary>
    public IntPtr Res;

    /// <summary>Flags.</summary>
    public uint Flags;
}

/// <summary>Represents a CUDA texture descriptor.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CudaTextureDescriptor
{
    /// <summary>Address mode 0.</summary>
    public int AddressMode0;

    /// <summary>Address mode 1.</summary>
    public int AddressMode1;

    /// <summary>Address mode 2.</summary>
    public int AddressMode2;

    /// <summary>Filter mode.</summary>
    public int FilterMode;

    /// <summary>Flags.</summary>
    public uint Flags;

    /// <summary>Max anisotropy.</summary>
    public uint MaxAnisotropy;

    /// <summary>Mipmap filter mode.</summary>
    public int MipmapFilterMode;

    /// <summary>Mipmap level bias.</summary>
    public float MipmapLevelBias;

    /// <summary>Min mipmap level clamp.</summary>
    public float MinMipmapLevelClamp;

    /// <summary>Max mipmap level clamp.</summary>
    public float MaxMipmapLevelClamp;

    /// <summary>Border color 0.</summary>
    public float BorderColor0;

    /// <summary>Border color 1.</summary>
    public float BorderColor1;

    /// <summary>Border color 2.</summary>
    public float BorderColor2;

    /// <summary>Border color 3.</summary>
    public float BorderColor3;
}

/// <summary>Represents a CUDA resource view descriptor.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct CudaResourceViewDescriptor
{
    /// <summary>Resource view format.</summary>
    public int Format;

    /// <summary>Width in elements.</summary>
    public nuint Width;

    /// <summary>Height in elements.</summary>
    public nuint Height;

    /// <summary>Depth in elements.</summary>
    public nuint Depth;

    /// <summary>First mipmap level.</summary>
    public uint FirstMipmapLevel;

    /// <summary>Last mipmap level.</summary>
    public uint LastMipmapLevel;

    /// <summary>First layer index.</summary>
    public uint FirstLayer;

    /// <summary>Last layer index.</summary>
    public uint LastLayer;

    /// <summary>Flags.</summary>
    public uint Flags;
}


