namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

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
        if (GpuKernelLoader.IsCpuFallbackMode)
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
    /// Evaluates <paramref name="node"/> for its checks when its evaluation can throw (<see cref="VectorEffects"/>), as
    /// <c>System.Linq</c> runs every projection that a count enumerates; a node that cannot throw is not evaluated.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The node can throw and the workspace stream is capturing a CUDA graph (checked before any work, so that the
    /// capture stays valid), or the workspace runs under the CPU fallback.
    /// </exception>
    public static void EvaluateIfMayThrow(IGpuBigIntVectorExpression node, GpuWorkspace workspace)
    {
        if (VectorEffects.MayThrow(node))
        {
            ThrowIfCapturingChecks(workspace);
            Evaluate(node, workspace);
        }
    }

    /// <inheritdoc cref="EvaluateIfMayThrow(IGpuBigIntVectorExpression, GpuWorkspace)"/>
    public static void EvaluateIfMayThrow(IGpuBigFloatVectorExpression node, GpuWorkspace workspace)
    {
        if (VectorEffects.MayThrow(node))
        {
            ThrowIfCapturingChecks(workspace);
            Evaluate(node, workspace);
        }
    }

    /// <inheritdoc cref="EvaluateIfMayThrow(IGpuBigIntVectorExpression, GpuWorkspace)"/>
    public static void EvaluateIfMayThrow(IGpuBoolVectorExpression node, GpuWorkspace workspace)
    {
        if (VectorEffects.MayThrow(node))
        {
            ThrowIfCapturingChecks(workspace);
            Evaluate(node, workspace);
        }
    }

    // The checks of a division, a square root or of the lengths read their result back from the device
    private static void ThrowIfCapturingChecks(GpuWorkspace workspace) =>
        CaptureChecks.ThrowIfCapturing(workspace,
            "The check of a division, a square root or the lengths of a counted vector",
            "Count a chain without them in a graph, or count this one without a graph.");

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

    /// <summary>Throws when the workspace stream is capturing a CUDA graph, which cannot record a host read.</summary>
    /// <exception cref="InvalidOperationException">The stream is capturing.</exception>
    public static void ThrowIfCapturing(GpuWorkspace workspace)
    {
        if (CaptureChecks.IsCapturing(workspace))
        {
            throw new InvalidOperationException(
                "The length of a filtered vector is known only on the device and cannot be read while a CUDA graph is " +
                "captured; reduce it (Count, Sum, ...) inside the graph instead.");
        }
    }
}
