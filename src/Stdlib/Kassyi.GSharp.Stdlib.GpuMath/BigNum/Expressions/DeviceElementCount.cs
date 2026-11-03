namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// The number of elements of a vector expression after evaluation (design D3 4.1): a host value, or a device word with
/// a host-side offset and limit, <c>count = clamp(*Base - Skip, 0, Limit)</c>.
/// </summary>
/// <remarks>
/// Skip and Take compose in closed form: <c>clamp(clamp(b - s, 0, L) - n, 0, ...)</c> is
/// <c>clamp(b - (s + n), 0, max(L - n, 0))</c>, and <c>min(clamp(b - s, 0, L), t)</c> is <c>clamp(b - s, 0, min(L, t))</c>,
/// so any number of them needs neither a kernel nor a word of memory.
/// </remarks>
/// <param name="Base">The device count word written by a <c>Where</c>, or <see cref="IntPtr.Zero"/> for a host count.</param>
/// <param name="Skip">The number of elements skipped after the device count.</param>
/// <param name="Limit">The upper bound of the count; the count itself when <paramref name="Base"/> is zero.</param>
internal readonly record struct DeviceElementCount(IntPtr Base, int Skip, int Limit)
{
    /// <summary>A count known on the host.</summary>
    public static DeviceElementCount Static(int length) => new(IntPtr.Zero, 0, length);

    /// <summary>Gets whether the count is known on the host.</summary>
    public bool IsStatic => Base == IntPtr.Zero;

    /// <summary>Gets the upper bound of the count, known on the host.</summary>
    public int Capacity => Limit;

    /// <summary>The count after skipping <paramref name="n"/> elements.</summary>
    public DeviceElementCount SkipFirst(int n) =>
        IsStatic ? Static(Math.Max(Limit - n, 0)) : new(Base, Skip + n, Math.Max(Limit - n, 0));

    /// <summary>The count after taking at most <paramref name="n"/> elements.</summary>
    public DeviceElementCount TakeFirst(int n) =>
        IsStatic ? Static(Math.Min(Limit, n)) : new(Base, Skip, Math.Min(Limit, n));

    /// <summary>
    /// Reads the count on the host: one synchronization of the workspace stream when the count is on the device.
    /// </summary>
    /// <exception cref="InvalidOperationException">The count is on the device and the stream is capturing a graph.</exception>
    public int Read(GpuWorkspace workspace)
    {
        if (IsStatic)
        {
            return Limit;
        }

        // Every host read goes through here (Materialize, the host element loops), so a capture is refused once
        DeviceCounted.ThrowIfCapturing(workspace);
        var n = (long)workspace.Provider.ReadWord(Base, 0, workspace.Stream) - Skip;
        return (int)Math.Clamp(n, 0, Limit);
    }

    /// <summary>The kernel argument for this count.</summary>
    public GpuBigIntVectorKernels.AotContexts.ElementCount ToContext() =>
        new() { Base = Base, Skip = Skip, Limit = Limit };

    /// <summary>
    /// The kernel argument for the scan and scatter kernels, whose class has its own host copy of the same context.
    /// </summary>
    public GpuStreamCompactionKernels.AotContexts.ElementCount ToScanContext() =>
        new() { Base = Base, Skip = Skip, Limit = Limit };
}

/// <summary>
/// The result of evaluating a vector node for another node: storage of <see cref="DeviceElementCount.Capacity"/>
/// elements, of which only the first <c>Count</c> are elements.
/// </summary>
internal readonly ref struct VectorEvaluation
{
    public VectorEvaluation(GpuBigIntArrayView storage, DeviceElementCount count)
    {
        Storage = storage;
        Count = count;
    }

    public GpuBigIntArrayView Storage { get; }

    public DeviceElementCount Count { get; }
}

/// <summary>The same for a floating-point vector.</summary>
internal readonly ref struct FloatVectorEvaluation
{
    public FloatVectorEvaluation(GpuBigFloatArrayView storage, DeviceElementCount count)
    {
        Storage = storage;
        Count = count;
    }

    public GpuBigFloatArrayView Storage { get; }

    public DeviceElementCount Count { get; }

    /// <summary>The number of elements to run a host loop over: the count, read when it is on the device.</summary>
    public int Elements(GpuWorkspace workspace) => Count.IsStatic ? Storage.Length : Count.Read(workspace);
}

/// <summary>The same for a vector of flags (1 = true, 0 = false).</summary>
internal readonly ref struct BoolVectorEvaluation
{
    public BoolVectorEvaluation(GpuBigUIntArrayView flags, DeviceElementCount count)
    {
        Flags = flags;
        Count = count;
    }

    public GpuBigUIntArrayView Flags { get; }

    public DeviceElementCount Count { get; }
}

/// <summary>
/// Implemented by the GpuMath vector nodes, which pass a device count to each other. A node writes only
/// <see cref="EvaluateCore"/>; its public <c>Evaluate</c> is that evaluation cut to the count.
/// </summary>
internal interface IDeviceCountedVector : IGpuBigIntVectorExpression
{
    /// <summary>
    /// Evaluates elements <paramref name="elementOffset"/> to <c>elementOffset + elementCount</c> of the capacity;
    /// the count says how many of them are elements.
    /// </summary>
    VectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult);

    GpuBigIntArrayView IGpuBigIntVectorExpression.Evaluate(GpuWorkspace workspace, int elementOffset,
        int? elementCount, GpuBigUIntArrayView targetResult) =>
        DeviceCounted.Materialize(workspace,
            DeviceCounted.Evaluate(this, workspace, elementOffset, elementCount, targetResult));
}

/// <summary>Implemented by the GpuMath floating-point vector nodes, as <see cref="IDeviceCountedVector"/>.</summary>
internal interface IDeviceCountedFloatVector : IGpuBigFloatVectorExpression
{
    FloatVectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigFloatArrayView targetResult);

    GpuBigFloatArrayView IGpuBigFloatVectorExpression.Evaluate(GpuWorkspace workspace, int elementOffset,
        int? elementCount, GpuBigFloatArrayView targetResult) =>
        DeviceCounted.Materialize(workspace,
            DeviceCounted.Evaluate(this, workspace, elementOffset, elementCount, targetResult));
}

/// <summary>Implemented by the GpuMath flag vector nodes, as <see cref="IDeviceCountedVector"/>.</summary>
internal interface IDeviceCountedBoolVector : IGpuBoolVectorExpression
{
    BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace);

    GpuBigUIntArrayView IGpuBoolVectorExpression.Evaluate(GpuWorkspace workspace) =>
        DeviceCounted.Materialize(workspace, DeviceCounted.Evaluate(this, workspace));
}

/// <summary>Evaluation of vector nodes with a device count, and the public view of the result.</summary>
internal static class DeviceCounted
{
    /// <summary>The message of <c>Length</c> when the count is known only on the device.</summary>
    public const string LengthUnknown =
        "The number of elements of a filtered vector is known only on the device. Use Capacity, TryGetLength or Count().";

    /// <summary>
    /// The <c>Length</c> of a node whose count may be known only on the device: its <c>TryGetLength</c>, or
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    public static int KnownLength(IGpuBigIntVectorExpression node) =>
        node.TryGetLength(out var length) ? length : throw new InvalidOperationException(LengthUnknown);

    /// <inheritdoc cref="KnownLength(IGpuBigIntVectorExpression)"/>
    public static int KnownLength(IGpuBigFloatVectorExpression node) =>
        node.TryGetLength(out var length) ? length : throw new InvalidOperationException(LengthUnknown);

    /// <summary>
    /// Throws when the workspace runs under the CPU fallback, where vectorized CUDA kernels cannot execute on host memory.
    /// </summary>
    /// <exception cref="NotSupportedException">The workspace uses the CPU fallback provider.</exception>
    public static void ThrowIfCpuFallback(GpuWorkspace workspace)
    {
        if (workspace.Provider is not GSharpKernelProvider || GpuKernelLoader.IsCpuFallbackMode)
        {
            throw new NotSupportedException(
                "Vectorized GpuMath expressions require a GPU workspace and are not supported under the CPU fallback.");
        }
    }

    /// <summary>
    /// Evaluates <paramref name="node"/> for another node; a node outside GpuMath has a host count (its length).
    /// </summary>
    public static VectorEvaluation Evaluate(IGpuBigIntVectorExpression node, GpuWorkspace workspace,
        int elementOffset = 0, int? elementCount = null, GpuBigUIntArrayView targetResult = default)
    {
        ThrowIfCpuFallback(workspace);
        if (node is IDeviceCountedVector counted)
        {
            return counted.EvaluateCore(workspace, elementOffset, elementCount, targetResult);
        }

        var storage = node.Evaluate(workspace, elementOffset, elementCount, targetResult);
        return new VectorEvaluation(storage, DeviceElementCount.Static(storage.Length));
    }

    /// <inheritdoc cref="Evaluate(IGpuBigIntVectorExpression, GpuWorkspace, int, int?, GpuBigUIntArrayView)"/>
    public static FloatVectorEvaluation Evaluate(IGpuBigFloatVectorExpression node, GpuWorkspace workspace,
        int elementOffset = 0, int? elementCount = null, GpuBigFloatArrayView targetResult = default)
    {
        ThrowIfCpuFallback(workspace);
        if (node is IDeviceCountedFloatVector counted)
        {
            return counted.EvaluateCore(workspace, elementOffset, elementCount, targetResult);
        }

        var storage = node.Evaluate(workspace, elementOffset, elementCount, targetResult);
        return new FloatVectorEvaluation(storage, DeviceElementCount.Static(storage.Length));
    }

    /// <inheritdoc cref="Evaluate(IGpuBigIntVectorExpression, GpuWorkspace, int, int?, GpuBigUIntArrayView)"/>
    public static BoolVectorEvaluation Evaluate(IGpuBoolVectorExpression node, GpuWorkspace workspace)
    {
        ThrowIfCpuFallback(workspace);
        if (node is IDeviceCountedBoolVector counted)
        {
            return counted.EvaluateCore(workspace);
        }

        var flags = node.Evaluate(workspace);
        return new BoolVectorEvaluation(flags, DeviceElementCount.Static(flags.Length));
    }

    /// <summary>
    /// The public result of an evaluation: the storage itself for a host count; for a device count, the count is read
    /// (one synchronization) and the storage cut to it.
    /// </summary>
    /// <exception cref="InvalidOperationException">The count is on the device and the stream is capturing a graph.</exception>
    public static GpuBigIntArrayView Materialize(GpuWorkspace workspace, VectorEvaluation evaluation)
    {
        if (evaluation.Count.IsStatic)
        {
            return evaluation.Storage;
        }

        return evaluation.Storage.Slice(0, evaluation.Count.Read(workspace));
    }

    /// <inheritdoc cref="Materialize(GpuWorkspace, VectorEvaluation)"/>
    public static GpuBigFloatArrayView Materialize(GpuWorkspace workspace, FloatVectorEvaluation evaluation)
    {
        if (evaluation.Count.IsStatic)
        {
            return evaluation.Storage;
        }

        return evaluation.Storage.Slice(0, evaluation.Count.Read(workspace));
    }

    /// <inheritdoc cref="Materialize(GpuWorkspace, VectorEvaluation)"/>
    public static GpuBigUIntArrayView Materialize(GpuWorkspace workspace, BoolVectorEvaluation evaluation)
    {
        if (evaluation.Count.IsStatic)
        {
            return evaluation.Flags;
        }

        return evaluation.Flags.Slice(0, evaluation.Count.Read(workspace));
    }

    /// <summary>
    /// The count of a node with two vector inputs that were evaluated over the same window (design J4): a device count
    /// when one side has one, the common count when both have the same one.
    /// </summary>
    /// <exception cref="NotSupportedException">Both counts are on the device and are not the same count.</exception>
    public static DeviceElementCount Combine(DeviceElementCount left, DeviceElementCount right)
    {
        if (right.IsStatic)
        {
            return left;
        }

        if (left.IsStatic)
        {
            return right;
        }

        return left == right
            ? left
            : throw new NotSupportedException(
                "Both vectors were filtered by different Where calls, so their lengths are known only on the device. " +
                "Combine the predicates into one Where (p.And(q)).");
    }

    /// <summary>Gets whether the workspace stream is capturing a CUDA graph.</summary>
    public static bool IsCapturing(GpuWorkspace workspace)
    {
        if (workspace.Stream == IntPtr.Zero || GpuKernelLoader.IsCpuFallbackMode)
        {
            return false;
        }

        CudaException.ThrowIfError(CudaDriverApi.cuStreamIsCapturing(workspace.Stream, out var status));
        return status != CUstreamCaptureStatus.None;
    }

    /// <summary>Throws when the workspace stream is capturing a CUDA graph, which cannot record a host read.</summary>
    /// <exception cref="InvalidOperationException">The stream is capturing.</exception>
    public static void ThrowIfCapturing(GpuWorkspace workspace)
    {
        if (IsCapturing(workspace))
        {
            throw new InvalidOperationException(
                "The length of a filtered vector is known only on the device and cannot be read while a CUDA graph is " +
                "captured; reduce it (Count, Sum, ...) inside the graph instead.");
        }
    }
}
