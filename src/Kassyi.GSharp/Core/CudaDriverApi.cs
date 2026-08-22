using System.Runtime.InteropServices;

namespace Kassyi.GSharp.Core;

public enum CUresult
{
    CUDA_SUCCESS = 0,
    CUDA_ERROR_INVALID_VALUE = 1,
    CUDA_ERROR_OUT_OF_MEMORY = 2,
    CUDA_ERROR_NOT_INITIALIZED = 3,
    CUDA_ERROR_DEINITIALIZED = 4,
    CUDA_ERROR_NO_DEVICE = 100,
    CUDA_ERROR_INVALID_DEVICE = 101,
    CUDA_ERROR_INVALID_IMAGE = 200,
    CUDA_ERROR_INVALID_CONTEXT = 201,
    CUDA_ERROR_CONTEXT_ALREADY_CURRENT = 202,
    CUDA_ERROR_MAP_FAILED = 205,
    CUDA_ERROR_UNMAP_FAILED = 206,
    CUDA_ERROR_ARRAY_IS_MAPPED = 207,
    CUDA_ERROR_ALREADY_MAPPED = 208,
    CUDA_ERROR_NO_BINARY_FOR_GPU = 209,
    CUDA_ERROR_ALREADY_ACQUIRED = 210,
    CUDA_ERROR_NOT_MAPPED = 211,
    CUDA_ERROR_NOT_MAPPED_AS_ARRAY = 212,
    CUDA_ERROR_NOT_MAPPED_AS_POINTER = 213,
    CUDA_ERROR_ECC_UNCORRECTABLE = 214,
    CUDA_ERROR_UNSUPPORTED_LIMIT = 215,
    CUDA_ERROR_CONTEXT_ALREADY_IN_USE = 216,
    CUDA_ERROR_INVALID_SOURCE = 300,
    CUDA_ERROR_FILE_NOT_FOUND = 301,
    CUDA_ERROR_SHARED_OBJECT_SYMBOL_NOT_FOUND = 302,
    CUDA_ERROR_SHARED_OBJECT_INIT_FAILED = 303,
    CUDA_ERROR_OPERATING_SYSTEM = 304,
    CUDA_ERROR_INVALID_HANDLE = 400,
    CUDA_ERROR_NOT_FOUND = 500,
    CUDA_ERROR_NOT_READY = 600,
    CUDA_ERROR_LAUNCH_FAILED = 700,
    CUDA_ERROR_LAUNCH_OUT_OF_RESOURCES = 701,
    CUDA_ERROR_LAUNCH_TIMEOUT = 702,
    CUDA_ERROR_LAUNCH_INCOMPATIBLE_TEXTURING = 703,
    CUDA_ERROR_UNKNOWN = 999
}

public class CudaException : Exception
{
    public CUresult Result { get; }

    public CudaException(CUresult result) : base($"CUDA Error: {result}")
    {
        Result = result;
    }

    public static void ThrowIfError(CUresult result)
    {
        if (result != CUresult.CUDA_SUCCESS)
        {
            throw new CudaException(result);
        }
    }
}

public static class CudaDriverApi
{
    private const string DllName = "nvcuda.dll";

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
    public static extern CUresult cuModuleLoadDataEx(out IntPtr module, byte[] image, uint numOptions, IntPtr[]? options, IntPtr[]? optionValues);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    public static extern CUresult cuModuleGetFunction(out IntPtr hfunc, IntPtr hmod, string name);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemAlloc_v2(out IntPtr dptr, IntPtr bytesize);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemFree_v2(IntPtr dptr);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemcpyHtoD_v2(IntPtr dstDevice, IntPtr srcHost, IntPtr ByteCount);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuMemcpyDtoH_v2(IntPtr dstHost, IntPtr srcDevice, IntPtr ByteCount);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern CUresult cuLaunchKernel(
        IntPtr f,
        uint gridDimX, uint gridDimY, uint gridDimZ,
        uint blockDimX, uint blockDimY, uint blockDimZ,
        uint sharedMemBytes,
        IntPtr hStream,
        IntPtr[] kernelParams,
        IntPtr extra);
}
