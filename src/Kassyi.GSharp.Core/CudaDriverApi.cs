// ReSharper disable UnusedMember.Global


using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Core;

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

public static class CudaDriverApi
{
    private const string DllName = "nvcuda.dll";

    public enum CuDeviceAttribute
    {
        ComputeCapabilityMajor = 75,
        ComputeCapabilityMinor = 76
    }

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuDeviceGetAttribute(out int pi, CuDeviceAttribute attrib, int dev);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuInit(uint flags);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuDeviceGet(out int device, int ordinal);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuCtxCreate_v2(out IntPtr pctx, uint flags, int dev);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuCtxDestroy_v2(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuCtxSetCurrent(IntPtr ctx);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuCtxGetCurrent(out IntPtr pctx);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuModuleLoadDataEx(out IntPtr module, byte[] image, uint numOptions,
        IntPtr[]? options, IntPtr[]? optionValues);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern CUresult cuModuleGetFunction(out IntPtr hfunc, IntPtr hmod, string name);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuModuleUnload(IntPtr hmod);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemAlloc_v2(out IntPtr dptr, IntPtr bytesize);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemFree_v2(IntPtr dptr);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemcpyHtoD_v2(IntPtr dstDevice, IntPtr srcHost, IntPtr byteCount);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemcpyDtoH_v2(IntPtr dstHost, IntPtr srcDevice, IntPtr byteCount);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemcpyDtoD_v2(IntPtr dstDevice, IntPtr srcDevice, IntPtr byteCount);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static unsafe extern CUresult cuLaunchKernel(
        IntPtr f,
        uint gridDimX, uint gridDimY, uint gridDimZ,
        uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes,
        IntPtr hStream,
        void** kernelParams,
        IntPtr extra);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static unsafe extern CUresult cuLaunchCooperativeKernel(
        IntPtr f,
        uint gridDimX, uint gridDimY, uint gridDimZ,
        uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes,
        IntPtr hStream,
        void** kernelParams);
}
