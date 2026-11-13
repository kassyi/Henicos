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
/// <para>
/// A kernel that allows oversized shared memory may go past the opt-in limit on a device that has an oversized shared
/// memory configuration: the first launch that needs it sets the function's shared memory mode
/// (<c>CU_FUNC_ATTRIBUTE_SHARED_MEMORY_MODE</c>) once. <see cref="SharedMemoryOptInPlan"/> makes the decision.
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
    public static void Prepare(IntPtr hfunc, uint sharedMemBytes, string kernelName) =>
        Prepare(hfunc, sharedMemBytes, kernelName, allowsOversizedSharedMemory: false);

    /// <summary>
    /// Opts <paramref name="hfunc"/> in to <paramref name="sharedMemBytes"/> of dynamic shared memory when the request
    /// is above the function's current limit, using the oversized shared memory of the device when the kernel allows it
    /// and the request needs it, or throws when the device cannot hold it.
    /// </summary>
    /// <param name="hfunc">The function about to be launched (<c>CUfunction</c>), with its context current.</param>
    /// <param name="sharedMemBytes">The dynamic shared memory per block that the launch requests.</param>
    /// <param name="kernelName">The kernel as <c>Type.Method</c>, for the exception message.</param>
    /// <param name="allowsOversizedSharedMemory">
    /// Whether the kernel allows an oversized shared memory configuration. A request that fits the opt-in limit makes
    /// the same driver calls either way; a larger one sets the function's shared memory mode once, on a device that
    /// has such a configuration.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The static shared memory of the function plus <paramref name="sharedMemBytes"/> exceeds the shared memory per
    /// block of the device that the kernel may use.
    /// </exception>
    public static void Prepare(IntPtr hfunc, uint sharedMemBytes, string kernelName, bool allowsOversizedSharedMemory)
    {
        // [PERF] A launch without dynamic shared memory never needs the opt-in
        if (sharedMemBytes == 0)
        {
            return;
        }

        var limits = s_functions.GetOrAdd(hfunc, static h => FunctionLimits.Query(h));
        if (!limits.ModeIsSet && sharedMemBytes <= (uint)limits.AllowedDynamicBytes)
        {
            return;
        }

        var plan = SharedMemoryOptInPlan.Decide(sharedMemBytes, allowsOversizedSharedMemory,
            limits.ReadState(allowsOversizedSharedMemory));
        switch (plan.Action)
        {
            case SharedMemoryOptInAction.RaiseDynamicLimit:
                limits.OptIn(hfunc);
                break;
            case SharedMemoryOptInAction.AllowOversized:
                limits.AllowOversized(hfunc);
                break;
            case SharedMemoryOptInAction.Reject:
                throw new ArgumentOutOfRangeException(nameof(sharedMemBytes),
                    FormatRejection(sharedMemBytes, limits.StaticBytes, plan, limits.DeviceMajor, limits.DeviceMinor,
                        kernelName));
        }
    }

    /// <summary>The message of the exception for a request that <paramref name="plan"/> rejects.</summary>
    internal static string FormatRejection(uint sharedMemBytes, int staticBytes, SharedMemoryOptInPlan plan,
        int deviceMajor, int deviceMinor, string kernelName)
    {
        var hint = plan.OversizedHintBytes > 0
            ? $"; this GPU holds up to {plan.OversizedHintBytes} bytes for a kernel with " +
              "[GpuKernel(AllowOversizedSharedMemory = true)]"
            : "";
        return $"G#: sharedMemBytes {sharedMemBytes} plus {staticBytes} bytes of static shared memory exceeds the " +
            $"{plan.LimitBytes} bytes of shared memory per block of this GPU (sm_{deviceMajor}{deviceMinor}){hint} " +
            $"({kernelName}).";
    }

    /// <summary>
    /// How many times <see cref="Prepare(IntPtr, uint, string, bool)"/> raised the dynamic limit of
    /// <paramref name="hfunc"/>.
    /// </summary>
    internal static int GetOptInCount(IntPtr hfunc) =>
        s_functions.TryGetValue(hfunc, out var limits) ? limits.OptInCount : 0;

    /// <summary>
    /// How many times <see cref="Prepare(IntPtr, uint, string, bool)"/> set the shared memory mode of
    /// <paramref name="hfunc"/>.
    /// </summary>
    internal static int GetModeSetCount(IntPtr hfunc) =>
        s_functions.TryGetValue(hfunc, out var limits) ? limits.ModeSetCount : 0;

    /// <summary>Forgets every function; their handles are invalid once the modules are unloaded.</summary>
    internal static void Clear() => s_functions.Clear();

    private sealed class FunctionLimits
    {
        private readonly Lock _lock = new();
        private const int NotQueried = -1;
        private volatile int _allowedDynamicBytes;
        private volatile bool _modeIsSet;
        private volatile int _oversizedBytesPerBlock = NotQueried;
        private int _optInCount;
        private int _modeSetCount;

        private FunctionLimits(int staticBytes, int allowedDynamicBytes, int device, int optInBytesPerBlock,
            int deviceMajor, int deviceMinor)
        {
            StaticBytes = staticBytes;
            Device = device;
            _allowedDynamicBytes = allowedDynamicBytes;
            OptInBytesPerBlock = optInBytesPerBlock;
            DeviceMajor = deviceMajor;
            DeviceMinor = deviceMinor;
        }

        /// <summary>The static shared memory of the function (<c>CU_FUNC_ATTRIBUTE_SHARED_SIZE_BYTES</c>).</summary>
        public int StaticBytes { get; }

        /// <summary>The device of the function's context (<c>CUdevice</c>).</summary>
        public int Device { get; }

        /// <summary>The shared memory per block of the device after an opt-in, static plus dynamic.</summary>
        public int OptInBytesPerBlock { get; }

        public int DeviceMajor { get; }

        public int DeviceMinor { get; }

        /// <summary>The dynamic shared memory that a launch may request without a further opt-in.</summary>
        public int AllowedDynamicBytes => _allowedDynamicBytes;

        /// <summary>The dynamic shared memory that a launch may request after the opt-in.</summary>
        public int MaxDynamicBytes => Math.Max(0, OptInBytesPerBlock - StaticBytes);

        /// <summary>Whether the shared memory mode of the function allows oversized shared memory.</summary>
        public bool ModeIsSet => _modeIsSet;

        public int OptInCount => Volatile.Read(ref _optInCount);

        public int ModeSetCount => Volatile.Read(ref _modeSetCount);

        // Only a kernel that allows oversized shared memory reads the device attribute, once per function
        public SharedMemoryFunctionState ReadState(bool allowsOversized) =>
            new(StaticBytes, _allowedDynamicBytes, OptInBytesPerBlock, allowsOversized ? OversizedBytesPerBlock() : 0,
                _modeIsSet);

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
            return new FunctionLimits(staticBytes, allowedDynamicBytes, device, optInBytesPerBlock, major, minor);
        }

        // A failed query (a driver that does not know the attribute) counts as a device without oversized shared
        // memory, as does a value not above the opt-in limit (SharedMemoryOptInPlan); a thread that lost the race
        // queries the same value again
        private int OversizedBytesPerBlock()
        {
            var bytes = _oversizedBytesPerBlock;
            if (bytes != NotQueried)
            {
                return bytes;
            }

            var result = CudaDriverApi.cuDeviceGetAttribute(out bytes,
                CudaDriverApi.CuDeviceAttribute.MaxOversizedSharedMemoryPerBlock, Device);
            bytes = result == CUresult.CudaSuccess ? Math.Max(0, bytes) : 0;
            _oversizedBytesPerBlock = bytes;
            return bytes;
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

        // Sets the oversized shared memory mode once; a thread that lost the race finds it set and returns
        public void AllowOversized(IntPtr hfunc)
        {
            lock (_lock)
            {
                if (_modeIsSet)
                {
                    return;
                }

                CudaException.ThrowIfError(CudaDriverApi.cuFuncSetAttribute(hfunc,
                    CuFunctionAttribute.SharedMemoryMode, SharedMemoryOptInPlan.AllowOversizedSharedMemoryMode));
                _modeIsSet = true;
                _modeSetCount++;
            }
        }
    }
}
