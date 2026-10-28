namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Lets a CUDA launch use the dynamic shared memory it requests beyond the default 48 KB per block, and rejects a
/// request that the device cannot hold before the launch (#545).
/// </summary>
/// <remarks>
/// <para>
/// <b>[WHY]:</b> CUDA allows at most 48 KB of shared memory per block unless the function opts in with
/// <c>cuFuncSetAttribute(CU_FUNC_ATTRIBUTE_MAX_DYNAMIC_SHARED_SIZE_BYTES)</c>; without it the launch fails with
/// <c>CUDA_ERROR_INVALID_VALUE</c>. The <c>sharedMemBytes</c> argument of the launch is the user's marker: the opt-in
/// adds no work, no synchronization and does not change the result, it only permits the amount the launch already names.
/// </para>
/// <para>
/// Each function is queried and opted in at most once (the state is cached per <c>CUfunction</c>), the first time a
/// launch requests more than the function currently allows. The opt-in raises the limit to the device maximum, so later
/// launches of any size compare two integers and make no driver call.
/// </para>
/// </remarks>
public static class DynamicSharedMemoryOptIn
{
    private static readonly ConcurrentDictionary<IntPtr, FunctionLimits> s_functions = new();

    /// <summary>
    /// Opts <paramref name="hfunc"/> in to <paramref name="sharedMemBytes"/> of dynamic shared memory when the request
    /// is above the function's current limit, or throws when the device cannot hold it.
    /// </summary>
    /// <param name="hfunc">The function about to be launched (<c>CUfunction</c>), with its context current.</param>
    /// <param name="sharedMemBytes">The dynamic shared memory per block that the launch requests.</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The static shared memory of the function plus <paramref name="sharedMemBytes"/> exceeds the opt-in shared memory
    /// per block of the device.
    /// </exception>
    public static void Prepare(IntPtr hfunc, uint sharedMemBytes, string kernelName)
    {
        // [PERF] A launch without dynamic shared memory never needs the opt-in
        if (sharedMemBytes == 0)
        {
            return;
        }

        var limits = s_functions.GetOrAdd(hfunc, static h => FunctionLimits.Query(h));
        if (sharedMemBytes <= (uint)limits.AllowedDynamicBytes)
        {
            return;
        }

        if (sharedMemBytes > (uint)limits.MaxDynamicBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(sharedMemBytes),
                $"G#: sharedMemBytes {sharedMemBytes} plus {limits.StaticBytes} bytes of static shared memory exceeds the " +
                $"{limits.OptInBytesPerBlock} bytes of shared memory per block of this GPU (sm_{limits.DeviceMajor}{limits.DeviceMinor}) ({kernelName}).");
        }

        limits.OptIn(hfunc);
    }

    /// <summary>How many times <see cref="Prepare"/> called <c>cuFuncSetAttribute</c> for <paramref name="hfunc"/>.</summary>
    internal static int GetOptInCount(IntPtr hfunc) =>
        s_functions.TryGetValue(hfunc, out var limits) ? limits.OptInCount : 0;

    /// <summary>Forgets every function; their handles are invalid once the modules are unloaded.</summary>
    internal static void Clear() => s_functions.Clear();

    private sealed class FunctionLimits
    {
        private readonly Lock _lock = new();
        private volatile int _allowedDynamicBytes;
        private int _optInCount;

        private FunctionLimits(int staticBytes, int allowedDynamicBytes, int optInBytesPerBlock, int deviceMajor,
            int deviceMinor)
        {
            StaticBytes = staticBytes;
            _allowedDynamicBytes = allowedDynamicBytes;
            OptInBytesPerBlock = optInBytesPerBlock;
            DeviceMajor = deviceMajor;
            DeviceMinor = deviceMinor;
        }

        /// <summary>The static shared memory of the function (<c>CU_FUNC_ATTRIBUTE_SHARED_SIZE_BYTES</c>).</summary>
        public int StaticBytes { get; }

        /// <summary>The shared memory per block of the device after an opt-in, static plus dynamic.</summary>
        public int OptInBytesPerBlock { get; }

        public int DeviceMajor { get; }

        public int DeviceMinor { get; }

        /// <summary>The dynamic shared memory that a launch may request without a further opt-in.</summary>
        public int AllowedDynamicBytes => _allowedDynamicBytes;

        /// <summary>The dynamic shared memory that a launch may request after the opt-in.</summary>
        public int MaxDynamicBytes => Math.Max(0, OptInBytesPerBlock - StaticBytes);

        public int OptInCount => Volatile.Read(ref _optInCount);

        public static FunctionLimits Query(IntPtr hfunc)
        {
            CudaException.ThrowIfError(
                CudaDriverApi.cuFuncGetAttribute(out var staticBytes, CuFunctionAttribute.SharedSizeBytes, hfunc));
            CudaException.ThrowIfError(CudaDriverApi.cuFuncGetAttribute(out var allowedDynamicBytes,
                CuFunctionAttribute.MaxDynamicSharedSizeBytes, hfunc));
            CudaException.ThrowIfError(CudaDriverApi.cuCtxGetDevice(out var device));
            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var optInBytesPerBlock,
                CudaDriverApi.CuDeviceAttribute.MaxSharedMemoryPerBlockOptin, device));
            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var major,
                CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMajor, device));
            CudaException.ThrowIfError(CudaDriverApi.cuDeviceGetAttribute(out var minor,
                CudaDriverApi.CuDeviceAttribute.ComputeCapabilityMinor, device));
            return new FunctionLimits(staticBytes, allowedDynamicBytes, optInBytesPerBlock, major, minor);
        }

        // Raises the limit to the device maximum once; a thread that lost the race finds it raised and returns
        public void OptIn(IntPtr hfunc)
        {
            lock (_lock)
            {
                var maxDynamicBytes = MaxDynamicBytes;
                if (_allowedDynamicBytes >= maxDynamicBytes)
                {
                    return;
                }

                CudaException.ThrowIfError(CudaDriverApi.cuFuncSetAttribute(hfunc,
                    CuFunctionAttribute.MaxDynamicSharedSizeBytes, maxDynamicBytes));
                _allowedDynamicBytes = maxDynamicBytes;
                _optInCount++;
            }
        }
    }
}
