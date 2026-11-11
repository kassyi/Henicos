namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>Exception thrown when a CUDA Driver API call returns a non-success status code.</summary>
public class CudaException(CUresult result, string? message = null, Exception? innerException = null)
    : Exception(message ?? $"CUDA Error: {result}", innerException)
{
    public CUresult Result { get; } = result;

    public static void ThrowIfError(CUresult result)
    {
        if (result != CUresult.CudaSuccess)
        {
            throw GpuKernelLoader.CreateExceptionForDriverError(result);
        }
    }
}
