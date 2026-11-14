[assembly:
    SuppressMessage("ReSharper", "UnusedMember.Global", Scope = "namespaceanddescendants",
        Target = "Kassyi.GSharp.Runtime.Interop", Justification = "Public API exposed for runtime consumers")]

namespace Kassyi.GSharp.Runtime.Interop;

/// <summary>P/Invoke bindings for NVIDIA CUDA Driver API functions (<c>nvcuda.dll</c> / <c>libcuda.so</c>).</summary>
// ReSharper disable once InconsistentNaming -- P/Invoke matches native CUDA API naming
public static partial class CudaDriverApi
{
    // NOTE FOR AI AGENTS & DEVELOPERS:
    // DO NOT declare [LibraryImport] P/Invoke methods manually in this file.
    // All CUDA Driver API bindings are automatically generated from 'src/NativeApi/cuda_driver_api.yml' at compile time.
    // To add or modify Driver API methods, declare them in the YAML file instead.

    private const string DllName = "nvcuda.dll";

    /// <summary>Flag for <c>cuMemHostAlloc</c>: memory is portable across all CUDA contexts.</summary>
    public const uint MemHostAllocPortable = 0x01;

    /// <summary>Flag for <c>cuMemHostAlloc</c>: memory is mapped into the CUDA address space (<c>cuMemHostGetDevicePointer</c>).</summary>
    public const uint MemHostAllocDeviceMap = 0x02;

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
        CanUseStreamWaitValueNor = 123,
        /// <summary>
        /// The shared memory per block in bytes that a function may use in the oversized shared memory mode (static
        /// plus dynamic). A GPU without an oversized configuration may return its opt-in limit
        /// (<see cref="MaxSharedMemoryPerBlockOptin"/>), so only a larger value means it has one.
        /// </summary>
        MaxOversizedSharedMemoryPerBlock = 150
    }
}


