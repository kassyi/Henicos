namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Queries CUDA occupancy via <c>cuOccupancyMaxActiveBlocksPerMultiprocessor</c> and caches the largest fitting
/// cooperative block size per function handle, device ordinal, and dynamic shared memory size.
/// </summary>
public static class CooperativeLaunchOccupancy
{
    private static readonly ConcurrentDictionary<(IntPtr Function, int Device, uint DynamicSharedMemBytes), int>
        s_blockSizes = new();

    /// <summary>
    /// Gets the cached cooperative block size for <paramref name="hfunc"/> on <paramref name="device"/> with
    /// <paramref name="dynamicSharedMemBytes"/>, or queries CUDA occupancy using
    /// <see cref="CooperativeBlockSize.DefaultCandidates"/> and caches the result.
    /// </summary>
    /// <param name="hfunc">Resolved <c>CUfunction</c> handle in the active CUDA context.</param>
    /// <param name="device">Active CUDA device ordinal (<c>CUdevice</c>).</param>
    /// <param name="dynamicSharedMemBytes">Dynamic shared memory per block in bytes.</param>
    /// <param name="kernelName">Kernel name for diagnostic error messages.</param>
    /// <returns>The largest fitting candidate block size.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="hfunc"/> is zero or <paramref name="kernelName"/> is empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">No candidate block size fits on an SM.</exception>
    public static int GetOrChooseBlockSize(
        IntPtr hfunc,
        int device,
        uint dynamicSharedMemBytes,
        string kernelName)
    {
        ValidateArguments(hfunc, kernelName);

        var key = (hfunc, device, dynamicSharedMemBytes);
        if (s_blockSizes.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var chosen = ChooseCore(
            hfunc,
            device,
            dynamicSharedMemBytes,
            kernelName,
            CooperativeBlockSize.DefaultCandidates);
        s_blockSizes.TryAdd(key, chosen);
        return chosen;
    }

    /// <summary>
    /// Queries CUDA occupancy for <paramref name="hfunc"/> using an explicit <paramref name="candidates"/> list without
    /// modifying the default block size cache.
    /// </summary>
    /// <param name="hfunc">Resolved <c>CUfunction</c> handle in the active CUDA context.</param>
    /// <param name="device">Active CUDA device ordinal (<c>CUdevice</c>).</param>
    /// <param name="dynamicSharedMemBytes">Dynamic shared memory per block in bytes.</param>
    /// <param name="kernelName">Kernel name for diagnostic error messages.</param>
    /// <param name="candidates">Candidate block sizes to evaluate.</param>
    /// <returns>The largest fitting candidate block size.</returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="hfunc"/> is zero, <paramref name="kernelName"/> is empty, or <paramref name="candidates"/> is
    /// empty.
    /// </exception>
    /// <exception cref="InvalidOperationException">No candidate block size fits on an SM.</exception>
    public static int ChooseBlockSize(
        IntPtr hfunc,
        int device,
        uint dynamicSharedMemBytes,
        string kernelName,
        ReadOnlySpan<int> candidates)
    {
        ValidateArguments(hfunc, kernelName);
        return ChooseCore(hfunc, device, dynamicSharedMemBytes, kernelName, candidates);
    }

    /// <summary>
    /// Clears cached block sizes when CUDA contexts are destroyed or unloaded so stale <c>CUfunction</c> handles are
    /// never reused.
    /// </summary>
    internal static void Clear() => s_blockSizes.Clear();

    internal static bool TryGetCachedBlockSize(
        IntPtr hfunc,
        int device,
        uint dynamicSharedMemBytes,
        out int blockSize) =>
        s_blockSizes.TryGetValue((hfunc, device, dynamicSharedMemBytes), out blockSize);

    internal static InvalidOperationException CreateUnsupportedException(
        in CooperativeKernelResources kernelInfo,
        int minCandidateBlockSize) =>
        new(CooperativeBlockSize.FormatFailureReason(in kernelInfo, minCandidateBlockSize));

    private static void ValidateArguments(IntPtr hfunc, string kernelName)
    {
        if (hfunc == IntPtr.Zero)
        {
            throw new ArgumentException("Kernel function handle cannot be zero.", nameof(hfunc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(kernelName);
    }

    private static int ChooseCore(
        IntPtr hfunc,
        int device,
        uint dynamicSharedMemBytes,
        string kernelName,
        ReadOnlySpan<int> candidates)
    {
        var result = CooperativeBlockSize.Choose(
            candidates,
            blockSize => QueryActiveBlocksPerSm(hfunc, blockSize, dynamicSharedMemBytes));
        if (result.IsSupported)
        {
            return result.BlockSize;
        }

        var resources = QueryKernelResources(hfunc, device, dynamicSharedMemBytes, kernelName);
        throw CreateUnsupportedException(in resources, result.MinCandidateBlockSize);
    }

    private static int QueryActiveBlocksPerSm(IntPtr hfunc, int blockSize, uint dynamicSharedMemBytes)
    {
        CudaException.ThrowIfError(
            CudaDriverApi.cuOccupancyMaxActiveBlocksPerMultiprocessor(
                out var numBlocks,
                hfunc,
                blockSize,
                dynamicSharedMemBytes));
        return numBlocks;
    }

    private static CooperativeKernelResources QueryKernelResources(
        IntPtr hfunc,
        int device,
        uint dynamicSharedMemBytes,
        string kernelName)
    {
        var attrs = GpuKernelLoader.GetKernelAttributes(hfunc);
        var major = QueryDeviceAttribute(device, CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMajor);
        var minor = QueryDeviceAttribute(device, CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMinor);
        var maxRegsSm = QueryDeviceAttribute(device, CudaDriverApi.CuDeviceAttribute.MaxRegistersPerMultiprocessor);
        var maxRegsBlock = QueryDeviceAttribute(device, CudaDriverApi.CuDeviceAttribute.MaxRegistersPerBlock);
        var maxSmemSm = QueryDeviceAttribute(
            device, CudaDriverApi.CuDeviceAttribute.MaxSharedMemoryPerMultiprocessor);
        var maxThreadsBlock = QueryDeviceAttribute(device, CudaDriverApi.CuDeviceAttribute.MaxThreadsPerBlock);

        return new CooperativeKernelResources(
            KernelName: kernelName,
            NumRegs: attrs.NumRegs,
            StaticSharedMemBytes: attrs.SharedSizeBytes,
            DynamicSharedMemBytes: (int)Math.Min(dynamicSharedMemBytes, int.MaxValue),
            DeviceMajor: major,
            DeviceMinor: minor,
            MaxRegistersPerMultiprocessor: maxRegsSm,
            MaxRegistersPerBlock: maxRegsBlock,
            MaxSharedMemPerMultiprocessor: maxSmemSm,
            MaxThreadsPerBlock: maxThreadsBlock);
    }

    private static int QueryDeviceAttribute(int device, CudaDriverApi.CuDeviceAttribute attribute) =>
        CudaDriverApi.cuDeviceGetAttribute(out var value, attribute, device) == CUresult.CudaSuccess
            ? value
            : 0;
}
