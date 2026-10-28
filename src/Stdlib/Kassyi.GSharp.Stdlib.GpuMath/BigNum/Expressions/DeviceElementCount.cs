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
    public int Read(GpuWorkspace workspace)
    {
        if (IsStatic)
        {
            return Limit;
        }

        var n = (long)workspace.Provider.ReadWord(Base, 0, workspace.Stream) - Skip;
        return (int)Math.Clamp(n, 0, Limit);
    }

    /// <summary>The kernel argument for this count.</summary>
    public GpuBigIntVectorKernels.AotContexts.ElementCount ToContext() =>
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

/// <summary>Implemented by the GpuMath vector nodes, which pass a device count to each other.</summary>
internal interface IDeviceCountedVector
{
    /// <summary>
    /// Evaluates elements <paramref name="elementOffset"/> to <c>elementOffset + elementCount</c> of the capacity;
    /// the count says how many of them are elements.
    /// </summary>
    VectorEvaluation EvaluateCore(GpuWorkspace workspace, int elementOffset, int? elementCount,
        GpuBigUIntArrayView targetResult);
}

/// <summary>Implemented by the GpuMath flag vector nodes.</summary>
internal interface IDeviceCountedBoolVector
{
    BoolVectorEvaluation EvaluateCore(GpuWorkspace workspace);
}

/// <summary>Evaluation of vector nodes with a device count, and the public view of the result.</summary>
internal static class DeviceCounted
{
    /// <summary>The message of <c>Length</c> when the count is known only on the device.</summary>
    public const string LengthUnknown =
        "The number of elements of a filtered vector is known only on the device. Use Capacity, TryGetLength or Count().";

    /// <summary>
    /// Evaluates <paramref name="node"/> for another node; a node outside GpuMath has a host count (its length).
    /// </summary>
    public static VectorEvaluation Evaluate(IGpuBigIntVectorExpression node, GpuWorkspace workspace,
        int elementOffset = 0, int? elementCount = null, GpuBigUIntArrayView targetResult = default)
    {
        if (node is IDeviceCountedVector counted)
        {
            return counted.EvaluateCore(workspace, elementOffset, elementCount, targetResult);
        }

        var storage = node.Evaluate(workspace, elementOffset, elementCount, targetResult);
        return new VectorEvaluation(storage, DeviceElementCount.Static(storage.Length));
    }

    /// <inheritdoc cref="Evaluate(IGpuBigIntVectorExpression, GpuWorkspace, int, int?, GpuBigUIntArrayView)"/>
    public static BoolVectorEvaluation Evaluate(IGpuBoolVectorExpression node, GpuWorkspace workspace)
    {
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

        ThrowIfCapturing(workspace);
        return evaluation.Storage.Slice(0, evaluation.Count.Read(workspace));
    }

    /// <inheritdoc cref="Materialize(GpuWorkspace, VectorEvaluation)"/>
    public static GpuBigUIntArrayView Materialize(GpuWorkspace workspace, BoolVectorEvaluation evaluation)
    {
        if (evaluation.Count.IsStatic)
        {
            return evaluation.Flags;
        }

        ThrowIfCapturing(workspace);
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

    private static void ThrowIfCapturing(GpuWorkspace workspace)
    {
        if (IsCapturing(workspace))
        {
            throw new InvalidOperationException(
                "The length of a filtered vector is known only on the device and cannot be read while a CUDA graph is " +
                "captured; reduce it (Count, Sum, ...) inside the graph instead.");
        }
    }
}
