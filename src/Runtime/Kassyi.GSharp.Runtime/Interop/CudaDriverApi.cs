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
    private const string DllName = "nvcuda.dll";

    public enum CuDeviceAttribute
    {
        MultiprocessorCount = 16,
        ComputeCapabilityMajor = 75,
        ComputeCapabilityMinor = 76
    }

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuDeviceGetAttribute(out int pi, CuDeviceAttribute attrib, int dev);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxGetDevice(out int device);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuInit(uint flags);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemGetInfo_v2(out nint free, out nint total);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuDeviceGet(out int device, int ordinal);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxCreate_v2(out IntPtr pctx, uint flags, int dev);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxDestroy_v2(IntPtr ctx);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuCtxPushCurrent_v2(IntPtr ctx);

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
    public static partial CUresult cuEventCreate(out IntPtr phEvent, uint flags);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuEventRecord(IntPtr hEvent, IntPtr hStream);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuEventSynchronize(IntPtr hEvent);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuEventElapsedTime(out float pMilliseconds, IntPtr hStart, IntPtr hEnd);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuEventDestroy_v2(IntPtr hEvent);

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
    public static partial CUresult cuMemcpyDtoDAsync_v2(IntPtr dstDevice, IntPtr srcDevice, IntPtr byteCount, IntPtr hStream);

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

    // CUDA Stream APIs
    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuStreamCreate(out IntPtr phStream, uint flags);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuStreamDestroy(IntPtr hStream);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuStreamSynchronize(IntPtr hStream);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemsetD8Async(IntPtr dptr, byte uc, IntPtr byteCount, IntPtr hStream);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemcpyHtoDAsync_v2(IntPtr dstDevice, IntPtr srcHost, IntPtr byteCount, IntPtr hStream);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuMemcpyDtoHAsync_v2(IntPtr dstHost, IntPtr srcDevice, IntPtr byteCount, IntPtr hStream);

    // CUDA Graph APIs
    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuStreamBeginCapture(IntPtr hStream, int mode);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuStreamEndCapture(IntPtr hStream, out IntPtr phGraph);

    [LibraryImport(DllName, EntryPoint = "cuGraphInstantiate_v2")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuGraphInstantiate(out IntPtr phGraphExec, IntPtr hGraph, IntPtr phErrorNode, IntPtr logBuffer, nuint bufferSize);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuGraphLaunch(IntPtr hGraphExec, IntPtr hStream);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuGraphExecDestroy(IntPtr hGraphExec);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuGraphDestroy(IntPtr hGraph);

    [LibraryImport(DllName)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    public static partial CUresult cuFuncGetAttribute(out int pi, CuFunctionAttribute attrib, IntPtr hfunc);
}
