using System.Runtime.CompilerServices;

[assembly: SuppressMessage("ReSharper", "UnusedMember.Global", Scope = "namespaceanddescendants", Target = "Kassyi.GSharp.Runtime.Interop", Justification = "Public API exposed for runtime consumers")]

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
    CudaErrorUnknown = 999
}

/// <summary>Exception thrown when a CUDA Driver API call returns a non-success status code.</summary>
public class CudaException(CUresult result) : Exception($"CUDA Error: {result}")
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
    private const string DllName = "nvcuda.dll";

    public enum CuDeviceAttribute
    {
        ComputeCapabilityMajor = 75,
        ComputeCapabilityMinor = 76
    }

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuDeviceGetAttribute(out int pi, CuDeviceAttribute attrib, int dev);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuInit(uint flags);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuDeviceGet(out int device, int ordinal);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxCreate_v2(out IntPtr pctx, uint flags, int dev);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxDestroy_v2(IntPtr ctx);

    // ReSharper disable once UnusedMember.Global -- Exposed for runtime context management
    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxPushCurrent_v2(IntPtr ctx);

    // ReSharper disable once UnusedMember.Global -- Exposed for runtime context management
    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxPopCurrent_v2(out IntPtr pctx);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxSetCurrent(IntPtr ctx);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxGetCurrent(out IntPtr pctx);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxSynchronize();

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuModuleLoadDataEx(out IntPtr module, byte[] image, uint numOptions,
        IntPtr[]? options, IntPtr[]? optionValues);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuModuleLoadDataEx(out IntPtr module, IntPtr image, uint numOptions,
        IntPtr[]? options, IntPtr[]? optionValues);

    [LibraryImport(DllName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuModuleGetFunction(out IntPtr hfunc, IntPtr hmod, string name);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuModuleUnload(IntPtr hmod);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemAlloc_v2(out IntPtr dptr, IntPtr bytesize);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemFree_v2(IntPtr dptr);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemsetD8_v2(IntPtr dptr, byte uc, IntPtr byteCount);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemcpyHtoD_v2(IntPtr dstDevice, IntPtr srcHost, IntPtr byteCount);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemcpyDtoH_v2(IntPtr dstHost, IntPtr srcDevice, IntPtr byteCount);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemcpyDtoD_v2(IntPtr dstDevice, IntPtr srcDevice, IntPtr byteCount);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static unsafe partial CUresult cuLaunchKernel(
        IntPtr f,
        uint gridDimX, uint gridDimY, uint gridDimZ,
        uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes,
        IntPtr hStream,
        void** kernelParams,
        IntPtr extra);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static unsafe partial CUresult cuLaunchCooperativeKernel(
        IntPtr f,
        uint gridDimX, uint gridDimY, uint gridDimZ,
        uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes,
        IntPtr hStream,
        void** kernelParams);
}

