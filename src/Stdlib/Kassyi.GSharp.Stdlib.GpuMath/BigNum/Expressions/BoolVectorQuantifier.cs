namespace Kassyi.GSharp.Stdlib.GpuMath.BigNum.Expressions;

/// <summary>
/// Any / All over the first Count flags of a vector: the kernels read the count on the device, so the
/// flags after it do not take part, and an empty vector gives false / true, as System.Linq.
/// </summary>
/// <remarks>
/// System.Linq stops at the deciding element, so a projection that throws after it never runs. For a chain that can
/// throw, the element exceptions are recorded in a <see cref="PendingThrows"/> scope instead of thrown, the kernels
/// find the lowest deciding index <c>D</c>, and the recorded exception is thrown only when its index <c>T</c> is not
/// after <c>D</c>.
/// </remarks>
internal static class BoolVectorQuantifier
{
    public static bool Evaluate(GpuWorkspace workspace, IGpuBoolVectorExpression source, bool all)
    {
        DeviceCounted.ThrowIfCpuFallback(workspace);
        if (source.Capacity == 0)
        {
            return EvaluateEmpty(workspace, source, all);
        }

        if (!VectorEffects.MayThrow(source))
        {
            return Reduce(workspace, source, all, decidingIndex: false) != 0;
        }

        if (!CanDefer(source))
        {
            // An enclosing scope must not take the exceptions of a chain that throws them itself
            using var _ = PendingThrows.Suspend();
            return Reduce(workspace, source, all, decidingIndex: false) != 0;
        }

        using var scope = PendingThrows.Begin();
        var pending = scope.Instance;
        uint decidedIndex;
        try
        {
            decidedIndex = Reduce(workspace, source, all, decidingIndex: true);
        }
        catch (Exception) when (pending.Exception is { } recorded)
        {
            // The element exception was raised first (a later length mismatch, a failure, or a value computed after the
            // recorded element): it wins, as in a sequential evaluation
            PendingThrows.ThrowRecorded(recorded);
            throw;
        }

        if (pending.Exception is { } exception && (uint)pending.MinIndex <= decidedIndex)
        {
            PendingThrows.ThrowRecorded(exception);
        }

        return decidedIndex == uint.MaxValue ? all : !all;
    }

    // Evaluates the flags and reduces them on the device: with decidingIndex, the lowest index of a deciding element
    // (true for Any, false for All) or uint.MaxValue; otherwise the result as 1 or 0
    private static uint Reduce(GpuWorkspace workspace, IGpuBoolVectorExpression source, bool all, bool decidingIndex)
    {
        var flags = DeviceCounted.Evaluate(source, workspace);
        using var result = new GpuBigUIntArray(workspace, 1, 1);
        var initial = decidingIndex ? uint.MaxValue : all ? 1u : 0u;
        workspace.Provider.WriteWord(result.DevicePointer, 0, initial, workspace.Stream);

        var ctx = new GpuBigIntVectorKernels.AotContexts.VectorBoolReduction
        {
            Flags = flags.Flags.ToProxy(),
            Result = result.ToProxy(),
            Count = flags.Count.ToContext()
        };

        var gridDimX = (uint)((flags.Count.Capacity + 255) / 256);
        var stream = workspace.Stream;
        switch (decidingIndex, all)
        {
            case (true, true):
                GpuBigIntVectorKernels.LaunchVectorAllTrueMinIndexKernelPtr(ctx, gridDimX, 1, 1, 256, 1, 1, 0, stream);
                break;
            case (true, false):
                GpuBigIntVectorKernels.LaunchVectorAnyTrueMinIndexKernelPtr(ctx, gridDimX, 1, 1, 256, 1, 1, 0, stream);
                break;
            case (false, true):
                GpuBigIntVectorKernels.LaunchVectorAllTrueKernelPtr(ctx, gridDimX, 1, 1, 256, 1, 1, 0, stream);
                break;
            default:
                GpuBigIntVectorKernels.LaunchVectorAnyTrueKernelPtr(ctx, gridDimX, 1, 1, 256, 1, 1, 0, stream);
                break;
        }

        return workspace.Provider.ReadWord(result.DevicePointer, 0, stream);
    }

    // Whether the element exceptions of the chain can be recorded by index: not through Where, which compacts the
    // elements into other indices, and not through a node implemented outside GpuMath, whose evaluation may evaluate
    // other vectors whose indices are not the indices of the flags. Each shared node is visited once.
    private static bool CanDefer(IGpuBoolVectorExpression source)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance) { source };
        var pending = new Stack<object>();
        pending.Push(source);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
                case VectorWhereExpression or FloatVectorWhereExpression:
                    return false;
                case IVectorOperator op:
                    for (var i = op.OperandCount - 1; i >= 0; i--)
                    {
                        var operand = op.GetOperand(i);
                        if (visited.Add(operand))
                        {
                            pending.Push(operand);
                        }
                    }

                    break;
                case GpuBigIntVectorLeafExpression or GpuBigFloatVectorLeafExpression:
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    // A vector with no capacity has no element: nothing is launched, and nothing is evaluated unless the evaluation
    // can throw (inputs of different lengths)
    private static bool EvaluateEmpty(GpuWorkspace workspace, IGpuBoolVectorExpression source, bool all)
    {
        DeviceCounted.EvaluateIfMayThrow(source, workspace);
        return all;
    }
}
